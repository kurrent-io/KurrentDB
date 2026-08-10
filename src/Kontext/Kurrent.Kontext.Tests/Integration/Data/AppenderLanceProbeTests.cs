// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Quack;
using Kurrent.Quack.Threading;

namespace Kurrent.Kontext.Tests.Data;

/// <summary>
/// Pins the capability boundary of the raw Quack <see cref="Appender"/> (duckdb_appender_create)
/// against the lance-attached catalog, probed live 2026-08-03:
/// - appending into a lance table WORKS (scalars and BLOB), but only by switching the session's
///   default catalog with USE — the C API has no catalog slot
/// - one appender flush is ONE lance commit regardless of row count; one SQL INSERT statement is
///   one commit — the batch-amortization the appender write path exists for
/// - FLOAT[N] ARRAY columns append via Add(span, CollectionType.Array) — the records indexer's
///   embedding column rides this
/// Guards Quack and lance-extension upgrades against silently moving that boundary.
/// </summary>
[Category("Integration")]
[Timeout(30_000)]
public class AppenderLanceProbeTests {
	[Test]
	public async ValueTask appends_to_native_table_control(CancellationToken cancellationToken) {
		// Arrange
		using var dir        = new TempDir();
		using var pool       = NewPool(dir.Path);
		using var connection = pool.Open();

		Exec(connection, "CREATE TABLE probe_native (id BIGINT, log_position UBIGINT, payload BLOB)");

		// Act
		var outcome = TryAppend(connection, "probe_native\0"u8);

		// Assert
		await Assert.That(outcome).IsEqualTo("OK");
		await Assert.That(Count(connection, "probe_native")).IsEqualTo(1L);
	}

	[Test]
	public async ValueTask appends_to_lance_table_through_use_redirection(CancellationToken cancellationToken) {
		// Arrange
		using var dir        = new TempDir();
		using var pool       = NewPool(dir.Path);
		using var connection = pool.Open();

		Exec(connection, "CREATE TABLE ldb.main.probe_lance (id BIGINT, log_position UBIGINT, payload BLOB)");

		// duckdb_appender_create has no catalog slot (Appender.cs passes schema = 0), so a
		// qualified name is parsed as a literal table name and the only route into the lance
		// catalog is switching the session's default catalog first.
		var qualified = TryAppend(connection, "ldb.main.probe_lance\0"u8);

		Exec(connection, "USE ldb");

		// Act
		var viaUse = TryAppend(connection, "probe_lance\0"u8);
		var rows   = Count(connection, "ldb.main.probe_lance");
		var blob   = Scalar(connection, "SELECT octet_length(payload) FROM ldb.main.probe_lance");

		// Assert — the appended BLOB survives the lance round trip byte-for-byte in length.
		await Assert.That(viaUse).IsEqualTo("OK");
		await Assert.That(rows).IsEqualTo(1L);
		await Assert.That(blob).IsEqualTo((long)Payload.Length);
		await Assert.That(qualified.StartsWith("FAILED at create", StringComparison.Ordinal)).IsTrue();
	}

	[Test]
	public async ValueTask one_flush_is_one_lance_commit(CancellationToken cancellationToken) {
		// Arrange
		using var dir        = new TempDir();
		using var pool       = NewPool(dir.Path);
		using var connection = pool.Open();

		Exec(connection, "CREATE TABLE ldb.main.probe_grain (id BIGINT, log_position UBIGINT, payload BLOB)");
		Exec(connection, "USE ldb");

		// Every lance commit writes one _versions/*.manifest under the dataset directory —
		// counting manifests counts commits.
		var baseline = CountManifests(dir.Path);

		// Act — 100 rows, ONE explicit flush. Kept well under the native appender's internal
		// auto-flush chunk (2048 rows) so exactly one flush reaches the engine.
		var appender = new Appender(connection, "probe_grain\0"u8);

		for (var i = 0; i < 100; i++) {
			var row = appender.CreateRow();
			row.Add((long)i);
			row.Add((ulong)i);
			row.Add(Payload);
			row.Dispose();
		}

		appender.Flush();
		appender.Dispose();

		var afterFlush = CountManifests(dir.Path);

		// Act — three single-row SQL INSERTs for comparison.
		Exec(connection, "INSERT INTO ldb.main.probe_grain VALUES (1000, 1000, ''::BLOB)");
		Exec(connection, "INSERT INTO ldb.main.probe_grain VALUES (1001, 1001, ''::BLOB)");
		Exec(connection, "INSERT INTO ldb.main.probe_grain VALUES (1002, 1002, ''::BLOB)");

		var afterInserts = CountManifests(dir.Path);
		var rows         = Count(connection, "ldb.main.probe_grain");

		// Assert — the amortization claim itself: commits scale with flushes, not rows.
		await Assert.That(rows).IsEqualTo(103L);
		await Assert.That(afterFlush - baseline).IsEqualTo(1);
		await Assert.That(afterInserts - afterFlush).IsEqualTo(3);
	}

	[Test]
	public async ValueTask buffered_appender_appends_to_lance_table_through_use_redirection(CancellationToken cancellationToken) {
		// Arrange
		using var dir        = new TempDir();
		using var pool       = NewPool(dir.Path);
		using var connection = pool.Open();

		Exec(connection, "CREATE TABLE ldb.main.probe_buffered (id BIGINT, content VARCHAR, embedding FLOAT[4])");
		Exec(connection, "USE ldb");

		var baseline = CountManifests(dir.Path);

		// Values exactly representable in float32, so the SQL equality below is exact, not approximate.
		ReadOnlySpan<float> embedding = [0.25f, -1.5f, 3.75f, 0.0625f];

		// Act — 100 rows, ONE flush, through the chunk-based appender. The chunk path
		// (duckdb_append_data_chunk) is a different native route than the raw appender's
		// per-value appends; this probe pins that it reaches the lance catalog at all.
		var appender = new BufferedAppender(connection, "probe_buffered\0"u8);

		for (var i = 0; i < 100; i++) {
			var row = appender.CreateRow();
			try {
				row.Add((long)i);
				row.Add("probe-content");
				row.Add(embedding, CollectionType.Array);
			} finally {
				row.Dispose();
			}
		}

		appender.Flush();
		appender.Dispose();

		var afterFlush = CountManifests(dir.Path);

		var matches = Scalar(
			connection,
			"""
			SELECT count(*) FROM ldb.main.probe_buffered
			WHERE embedding = CAST([0.25, -1.5, 3.75, 0.0625] AS FLOAT[4])
			  AND content = 'probe-content'
			""");

		// Assert — chunk-append reaches lance, the FLOAT[N] ARRAY survives element-exact
		// beside its scalars, and commits scale with flushes, not rows.
		await Assert.That(matches).IsEqualTo(100L);
		await Assert.That(afterFlush - baseline).IsEqualTo(1);
	}

	[Test]
	public async ValueTask appends_float_array_column_through_use_redirection(CancellationToken cancellationToken) {
		// Arrange
		using var dir        = new TempDir();
		using var pool       = NewPool(dir.Path);
		using var connection = pool.Open();

		Exec(connection, "CREATE TABLE ldb.main.probe_array (id BIGINT, content VARCHAR, embedding FLOAT[4])");
		Exec(connection, "USE ldb");

		// Values exactly representable in float32, so the SQL equality below is exact, not approximate.
		ReadOnlySpan<float> embedding = [0.25f, -1.5f, 3.75f, 0.0625f];

		// Act
		var appender = new Appender(connection, "probe_array\0"u8);
		var row      = appender.CreateRow();
		row.Add(1L);
		row.Add("probe-content");
		row.Add(embedding, CollectionType.Array);
		row.Dispose();
		appender.Flush();
		appender.Dispose();

		var matches = Scalar(
			connection,
			"""
			SELECT count(*) FROM ldb.main.probe_array
			WHERE embedding = CAST([0.25, -1.5, 3.75, 0.0625] AS FLOAT[4])
			  AND content = 'probe-content'
			""");

		// Assert — the FLOAT[N] ARRAY survives the lance round trip element-exact beside its scalars.
		await Assert.That(matches).IsEqualTo(1L);
	}

	static ReadOnlySpan<byte> Payload => "probe-payload"u8;

	static string TryAppend(DuckDBAdvancedConnection connection, ReadOnlySpan<byte> tableNameUtf8) {
		var stage = "create";
		try {
			var appender = new Appender(connection, tableNameUtf8);
			stage = "append";
			var row = appender.CreateRow();
			row.Add(1L);
			row.Add(100UL);
			row.Add(Payload);
			row.Dispose();
			stage = "flush";
			appender.Flush();
			stage = "dispose";
			appender.Dispose();
			return "OK";
		} catch (Exception ex) {
			return $"FAILED at {stage}: {ex.GetType().Name}: {ex.Message}";
		}
	}

	static void Exec(DuckDBAdvancedConnection connection, string sql) {
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.ExecuteNonQuery();
	}

	static long Count(DuckDBAdvancedConnection connection, string table) =>
		Scalar(connection, $"SELECT count(*) FROM {table}");

	static long Scalar(DuckDBAdvancedConnection connection, string sql) {
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return (long)command.ExecuteScalar()!;
	}

	static int CountManifests(string storagePath) =>
		Directory.GetFiles(storagePath, "*.manifest", SearchOption.AllDirectories).Length;

	static KontextConnectionPool NewPool(string dir) =>
		new($"Data Source={Path.Combine(dir, "engine.db")};access_mode=READ_WRITE", dir);

	/// <summary>A unique temp directory owned by one test; deleted on dispose.</summary>
	sealed class TempDir : IDisposable {
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kontext-appender-probe", Guid.NewGuid().ToString("N"));

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
