// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Pins the two engine behaviors a single all-in-one writer MERGE depends on, probed live against
/// the lance catalog:
/// 1. conditional WHEN arms — <c>WHEN NOT MATCHED AND condition</c> must insert selectively and
///    do NOTHING for fold-only source rows that match no target
/// 2. commit granularity — how many lance commits one duck transaction with several statements
///    produces (per statement vs per transaction), measured by manifest counting
/// </summary>
[Category("Integration")]
[Timeout(30_000)]
public class MergeLanceProbeTests {
	[Test]
	public async ValueTask conditional_merge_arms_insert_update_and_skip_selectively(CancellationToken cancellationToken) {
		// Arrange — one pre-existing row; the source carries one plain insert, one insert that is
		// already retracted (same-batch retain+retract), one fold-only row against the existing
		// target, and one fold-only ghost that matches nothing and must do NOTHING.
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();

		Exec(connection, "CREATE TABLE ldb.main.probe_merge (id VARCHAR, content VARCHAR, is_retracted BOOLEAN, retracted_at BIGINT, log_position BIGINT)");
		Exec(connection, "INSERT INTO ldb.main.probe_merge VALUES ('e1', 'old', false, NULL, 1)");

		// Act — the writer's prospective shape: facet-guarded arms over an unnest source.
		using (var command = connection.CreateCommand()) {
			command.CommandText =
				"""
				MERGE INTO ldb.main.probe_merge AS t
				USING (SELECT unnest(CAST($ids AS VARCHAR[])) AS id,
				              unnest(CAST($contents AS VARCHAR[])) AS content,
				              unnest(CAST($retracted AS BIGINT[])) AS retracted_at,
				              unnest(CAST($positions AS BIGINT[])) AS log_position) AS s
				ON t.id = s.id
				WHEN NOT MATCHED AND s.content IS NOT NULL THEN INSERT (id, content, is_retracted, retracted_at, log_position)
				    VALUES (s.id, s.content, s.retracted_at IS NOT NULL, s.retracted_at, s.log_position)
				WHEN MATCHED THEN UPDATE SET
				    content      = CASE WHEN s.content IS NOT NULL THEN s.content ELSE t.content END
				  , is_retracted = t.is_retracted OR s.retracted_at IS NOT NULL
				  , retracted_at = coalesce(s.retracted_at, t.retracted_at)
				  , log_position = s.log_position
				""";
			command.Parameters.Add(new("ids", new List<string> { "n1", "n2", "e1", "ghost" }));
			command.Parameters.Add(new("contents", new List<string?> { "fresh insert", "born retracted", null, null }));
			command.Parameters.Add(new("retracted", new List<long?> { null, 333, 111, 222 }));
			command.Parameters.Add(new("positions", new List<long> { 10, 40, 20, 30 }));
			command.ExecuteNonQuery();
		}

		// Assert — the ghost never landed; each arm did exactly its job.
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.probe_merge")).IsEqualTo(3L);
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.probe_merge WHERE id = 'ghost'")).IsEqualTo(0L);

		// n1: plain insert, live.
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.probe_merge WHERE id = 'n1' AND content = 'fresh insert' AND is_retracted = false")).IsEqualTo(1L);

		// n2: inserted already retracted — the terminal-state insert.
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.probe_merge WHERE id = 'n2' AND is_retracted = true AND retracted_at = 333")).IsEqualTo(1L);

		// e1: fold-only match — content untouched, retraction folded, position re-stamped.
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.probe_merge WHERE id = 'e1' AND content = 'old' AND is_retracted = true AND retracted_at = 111 AND log_position = 20")).IsEqualTo(1L);
	}

	[Test]
	public async ValueTask transaction_commit_granularity_on_lance(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);
		using var connection  = dataSources.OpenLanceWriter();

		Exec(connection, "CREATE TABLE ldb.main.probe_txn (id BIGINT)");

		var baseline = CountManifests(dir.Path);

		// Act — FOUR statements inside ONE duck transaction (the writer's current shape).
		using (var tx = connection.BeginTransaction()) {
			Exec(connection, "INSERT INTO ldb.main.probe_txn VALUES (1)");
			Exec(connection, "INSERT INTO ldb.main.probe_txn VALUES (2)");
			Exec(connection, "INSERT INTO ldb.main.probe_txn VALUES (3)");
			Exec(connection, "INSERT INTO ldb.main.probe_txn VALUES (4)");
			tx.CommitOnDispose();
		}

		var afterTx = CountManifests(dir.Path);

		// Act — two autocommit statements as the control (known: one commit each).
		Exec(connection, "INSERT INTO ldb.main.probe_txn VALUES (5)");
		Exec(connection, "INSERT INTO ldb.main.probe_txn VALUES (6)");

		var afterAuto = CountManifests(dir.Path);

		// The measurement IS the result — the deltas answer per-statement vs per-transaction.
		Console.WriteLine($"PROBE-TXN baseline={baseline} tx_delta={afterTx - baseline} autocommit_delta={afterAuto - afterTx}");

		// Assert — correctness only; the granularity numbers are read from the output.
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.probe_txn")).IsEqualTo(6L);
	}

	static void Exec(Kurrent.Quack.DuckDBAdvancedConnection connection, string sql) {
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}

	static long Scalar(Kurrent.Quack.DuckDBAdvancedConnection connection, string sql) {
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return (long)command.ExecuteScalar()!;
	}

	static int CountManifests(string storagePath) =>
		Directory.GetFiles(storagePath, "*.manifest", SearchOption.AllDirectories).Length;

	static KontextDataSource NewDataSources(string dir) =>
		MemorySeeding.NewDataSources(dir);

	/// <summary>A unique temp directory owned by one test; deleted on dispose.</summary>
	sealed class TempDir : IDisposable {
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kontext-merge-probe", Guid.NewGuid().ToString("N"));

		public TempDir() => Directory.CreateDirectory(Path);

		public void Dispose() {
			try {
				if (Directory.Exists(Path))
					Directory.Delete(Path, recursive: true);
			} catch (IOException) {
				// Best-effort cleanup; a lingering native handle must not fail the test.
			} catch (UnauthorizedAccessException) {
				// Best-effort cleanup.
			}
		}
	}
}
