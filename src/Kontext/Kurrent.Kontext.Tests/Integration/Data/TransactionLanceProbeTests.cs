// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Quack;
using Kurrent.Quack.Threading;
using Kurrent.Surge;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Pins how duck transactions interact with the lance catalog, probed live 2026-08-10 —
/// the facts the records indexer's checkpoint design stands on:
/// 1. a transaction that writes the lance catalog CANNOT also write another attached
///    database — the engine refuses with "a single transaction can only write to a single
///    attached database" (so a lance write plus an engine-native checkpoint can never share
///    a transaction)
/// 2. within the lance catalog, ROLLBACK reverts writes across MULTIPLE lance tables —
///    including a BufferedAppender flush; lance writes participate in the duck transaction
/// 3. commit granularity is one lance commit PER TABLE per transaction (each dataset gets
///    its own manifest; atomicity across datasets holds on the rollback path, while a crash
///    inside commit processing between the two native dataset commits remains the one
///    unprobeable window)
/// </summary>
[Category("Integration")]
[Timeout(30_000)]
public class TransactionLanceProbeTests {
	[Test]
	public async ValueTask cross_catalog_write_transaction_is_refused(CancellationToken cancellationToken) {
		// Arrange
		using var dir        = new TempDir();
		using var pool       = NewPool(dir.Path);
		using var connection = pool.Open();

		Exec(connection, "CREATE TABLE probe_ckpt_native (position BIGINT)");
		Exec(connection, "CREATE TABLE ldb.main.probe_rb (id BIGINT)");

		// Act — one duck transaction attempting to write both catalogs.
		string? refusal = null;

		using (connection.BeginTransaction()) {
			Exec(connection, "INSERT INTO ldb.main.probe_rb VALUES (1)");
			try {
				Exec(connection, "INSERT INTO probe_ckpt_native VALUES (10)");
			} catch (Exception ex) {
				refusal = ex.Message;
			}
		}

		// Assert — the engine refuses the second catalog, and the aborted transaction reverts
		// the lance write with it. A lance write and an engine-native checkpoint can never
		// share a transaction.
		await Assert.That(refusal).IsNotNull();
		await Assert.That(refusal!).Contains("single transaction can only write to a single attached database");
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.probe_rb")).IsEqualTo(0L);
		await Assert.That(Scalar(connection, "SELECT count(*) FROM probe_ckpt_native")).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask transaction_across_two_lance_tables(CancellationToken cancellationToken) {
		// Arrange
		using var dir        = new TempDir();
		using var pool       = NewPool(dir.Path);
		using var connection = pool.Open();

		Exec(connection, "CREATE TABLE ldb.main.probe_two_a (id BIGINT)");
		Exec(connection, "CREATE TABLE ldb.main.probe_two_b (id BIGINT)");

		var baselineA = CountManifests(dir.Path, "probe_two_a.lance");
		var baselineB = CountManifests(dir.Path, "probe_two_b.lance");

		// Act — commit phase: both tables in one transaction.
		using (var tx = connection.BeginTransaction()) {
			Exec(connection, "INSERT INTO ldb.main.probe_two_a VALUES (1)");
			Exec(connection, "INSERT INTO ldb.main.probe_two_b VALUES (1)");
			tx.CommitOnDispose();
		}

		var commitDeltaA = CountManifests(dir.Path, "probe_two_a.lance") - baselineA;
		var commitDeltaB = CountManifests(dir.Path, "probe_two_b.lance") - baselineB;

		// Act — rollback phase: both tables in one transaction, dispose without commit.
		using (connection.BeginTransaction()) {
			Exec(connection, "INSERT INTO ldb.main.probe_two_a VALUES (2)");
			Exec(connection, "INSERT INTO ldb.main.probe_two_b VALUES (2)");
		}

		var rowsA = Scalar(connection, "SELECT count(*) FROM ldb.main.probe_two_a");
		var rowsB = Scalar(connection, "SELECT count(*) FROM ldb.main.probe_two_b");

		// Assert — commit lands both with one lance commit per dataset; rollback reverts BOTH
		// lance tables (only the committed row remains in each).
		await Assert.That(commitDeltaA).IsEqualTo(1);
		await Assert.That(commitDeltaB).IsEqualTo(1);
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.probe_two_a WHERE id = 1")).IsEqualTo(1L);
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.probe_two_b WHERE id = 1")).IsEqualTo(1L);
		await Assert.That(rowsA).IsEqualTo(1L);
		await Assert.That(rowsB).IsEqualTo(1L);
	}

	[Test]
	public async ValueTask appender_flush_and_checkpoint_insert_in_one_transaction(CancellationToken cancellationToken) {
		// Arrange
		using var dir        = new TempDir();
		using var pool       = NewPool(dir.Path);
		using var connection = pool.Open();

		Exec(connection, "CREATE TABLE ldb.main.probe_app_data (id BIGINT)");
		Exec(connection, "CREATE TABLE ldb.main.probe_app_ckpt (position BIGINT)");
		Exec(connection, "USE ldb");

		var baseline = CountManifests(dir.Path);

		// Act — rollback phase: the records indexer's prospective shape, then dispose without commit.
		using (connection.BeginTransaction()) {
			AppendOneRow(connection, id: 1);
			Exec(connection, "INSERT INTO ldb.main.probe_app_ckpt VALUES (1)");
		}

		var dataAfterRollback = Scalar(connection, "SELECT count(*) FROM ldb.main.probe_app_data");
		var ckptAfterRollback = Scalar(connection, "SELECT count(*) FROM ldb.main.probe_app_ckpt");

		// Act — commit phase: same shape, committed.
		using (var tx = connection.BeginTransaction()) {
			AppendOneRow(connection, id: 2);
			Exec(connection, "INSERT INTO ldb.main.probe_app_ckpt VALUES (2)");
			tx.CommitOnDispose();
		}

		// Assert — the indexer's target shape holds: rollback reverts the appender flush AND
		// the checkpoint insert together; commit lands both, one lance commit per dataset.
		await Assert.That(dataAfterRollback).IsEqualTo(0L);
		await Assert.That(ckptAfterRollback).IsEqualTo(0L);
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.probe_app_data WHERE id = 2")).IsEqualTo(1L);
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.probe_app_ckpt WHERE position = 2")).IsEqualTo(1L);
		await Assert.That(CountManifests(dir.Path) - baseline).IsEqualTo(2);
	}

	[Test]
	public async ValueTask checkpoint_store_works_unchanged_on_a_lance_redirected_connection(CancellationToken cancellationToken) {
		// Arrange
		using var dir        = new TempDir();
		using var pool       = NewPool(dir.Path);
		using var connection = pool.Open();

		Exec(connection, "USE ldb");

		var checkpoints = new KontextCheckpointStore("records-probe");

		// Act — the store's own DDL and statements, unqualified, landing in the lance catalog:
		// PRIMARY KEY DDL, INSERT..ON CONFLICT DO NOTHING, and the filtered monotonic UPDATE.
		checkpoints.EnsureSchema(connection);

		var beforeAnyStore = checkpoints.Load(connection);

		checkpoints.Store(connection, RecordPosition.ForLog(100));
		var afterFirst = checkpoints.Load(connection);

		// A replayed older batch must be a no-op, not an error.
		checkpoints.Store(connection, RecordPosition.ForLog(50));
		var afterStale = checkpoints.Load(connection);

		var inLance = Scalar(connection, "SELECT count(*) FROM ldb.main.checkpoints WHERE key = 'records-probe'");

		// Assert — the class works unchanged; the connection alone decides the catalog.
		await Assert.That(beforeAnyStore).IsEqualTo(RecordPosition.Unset);
		await Assert.That((ulong?)afterFirst).IsEqualTo(100UL);
		await Assert.That((ulong?)afterStale).IsEqualTo(100UL);
		await Assert.That(inLance).IsEqualTo(1L);
	}

	static void AppendOneRow(DuckDBAdvancedConnection connection, long id) {
		var appender = new BufferedAppender(connection, "probe_app_data\0"u8);
		var row      = appender.CreateRow();
		try {
			row.Add(id);
		} finally {
			row.Dispose();
		}

		appender.Flush();
		appender.Dispose();
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

	static int CountManifests(string storagePath, string? dataset = null) =>
		Directory.GetFiles(dataset is null ? storagePath : Path.Combine(storagePath, dataset), "*.manifest", SearchOption.AllDirectories).Length;

	static KontextConnectionPool NewPool(string dir) =>
		new($"Data Source={Path.Combine(dir, "engine.db")};access_mode=READ_WRITE", dir);

	/// <summary>A unique temp directory owned by one test; deleted on dispose.</summary>
	sealed class TempDir : IDisposable {
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kontext-txn-probe", Guid.NewGuid().ToString("N"));

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
