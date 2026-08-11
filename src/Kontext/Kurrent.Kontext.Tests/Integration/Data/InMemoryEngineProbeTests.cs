// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Quack;
using Kurrent.Quack.Threading;
using Kurrent.Surge;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Pins that the pool needs NO engine file: everything durable lives in lance (tables AND
/// checkpoints), so the duck engine is a pure compute surface — each connection opens an
/// in-memory catalog and the lance ATTACH provides all shared state. Verifies the full writer
/// shape (appender flush + checkpoint MERGE in one transaction) and that a rented reader —
/// its own private in-memory catalog — sees the writer's lance commits.
/// </summary>
[Category("Integration")]
[Timeout(30_000)]
public class InMemoryEngineProbeTests {
	[Test]
	public async ValueTask writer_shape_works_without_an_engine_file(CancellationToken cancellationToken) {
		// Arrange — no Data Source file: the engine catalog is in-memory, per connection.
		using var dir  = new TempDir();
		using var pool = new KontextConnectionPool("Data Source=:memory:", dir.Path);

		await using var connection = pool.OpenLanceWriter();

		Exec(connection, "CREATE TABLE IF NOT EXISTS ldb.main.probe_mem (id BIGINT, content VARCHAR)");

		var checkpoints = new KontextCheckpointStore("in-memory-probe");
		checkpoints.EnsureSchema(connection);

		// Act — the indexer's full transaction shape on the memory-engine connection.
		using (var tx = connection.BeginTransaction()) {
			var appender = new BufferedAppender(connection, "probe_mem\0"u8);
			var row      = appender.CreateRow();
			try {
				row.Add(1L);
				row.Add("probe-content");
			} finally {
				row.Dispose();
			}

			appender.Flush();
			appender.Dispose();

			checkpoints.Store(connection, RecordPosition.ForLog(100));
			tx.CommitOnDispose();
		}

		// Act — rollback phase: both revert together, exactly as on a file-backed engine.
		using (connection.BeginTransaction()) {
			Exec(connection, "INSERT INTO ldb.main.probe_mem VALUES (2, 'rolled-back')");
			checkpoints.Store(connection, RecordPosition.ForLog(205));
		}

		// Assert — committed data and checkpoint hold; rolled-back advances do not.
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.probe_mem")).IsEqualTo(1L);
		await Assert.That((ulong?)checkpoints.Load(connection)).IsEqualTo(100UL);
	}

	[Test]
	public async ValueTask rented_reader_sees_the_writers_lance_commits(CancellationToken cancellationToken) {
		// Arrange — writer commits on its own connection; the reader rents a DIFFERENT
		// connection whose private in-memory catalog shares nothing but the lance ATTACH.
		using var dir  = new TempDir();
		using var pool = new KontextConnectionPool("Data Source=:memory:", dir.Path);

		await using (var connection = pool.OpenLanceWriter()) {
			Exec(connection, "CREATE TABLE IF NOT EXISTS ldb.main.probe_shared (id BIGINT)");
			Exec(connection, "INSERT INTO ldb.main.probe_shared VALUES (42)");
		}

		// Act
		var seen = await pool.ExecuteAsync(
			connection => Scalar(connection, "SELECT count(*) FROM ldb.main.probe_shared WHERE id = 42"),
			cancellationToken);

		// Assert — lance is the shared state; the engine catalogs never needed to be.
		await Assert.That(seen).IsEqualTo(1L);
	}

	static void Exec(DuckDBAdvancedConnection connection, string sql) {
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}

	static long Scalar(DuckDBAdvancedConnection connection, string sql) {
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return (long)command.ExecuteScalar()!;
	}

	/// <summary>A unique temp directory owned by one test; deleted on dispose.</summary>
	sealed class TempDir : IDisposable {
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kontext-inmemory-probe", Guid.NewGuid().ToString("N"));

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
