// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Modules.Records;
using Kurrent.Kontext.Modules.Records.Data;
using Kurrent.Quack;
using KurrentDB.Core.Data;
using KurrentDB.Core.TransactionLog.LogRecords;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kurrent.Kontext.Tests.Modules.Records;

[Category("Integration")]
[Timeout(30_000)]
public class KontextRecordsWriterTests {
	[Test]
	public async ValueTask writes_one_row_per_decodable_record_with_one_lance_commit(CancellationToken cancellationToken) {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var schema = new KontextRecordsSchema(pool, new() { Dimension = 4 });
		await schema.CreateAsync(cancellationToken);

		await using var connection = pool.Open();
		Exec(connection, "USE ldb");

		using var writer = NewWriter(connection);

		var content           = """{"total": 42}""";
		var timestamp         = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
		var expectedCreatedAt = new DateTimeOffset(timestamp).ToUnixTimeMilliseconds();
		var expectedEmbedding = FakeEmbeddingGenerator.Embed(content);

		var json  = NewRecord(logPosition: 100, "orders-1", "OrderPlaced", content, isJson: true, timestamp);
		var bytes = NewRecord(logPosition: 101, "orders-1", "OrderSnapshot", "raw-bytes", isJson: false, timestamp);

		var baseline = CountManifests(dir.Path);

		// Act
		var written = await writer.ProjectAsync([json, bytes], cancellationToken);

		// Assert — the JSON record lands whole (every column), the undecodable one never lands,
		// and the batch cost exactly one lance commit.
		using var command = connection.CreateCommand();
		command.CommandText =
			"""
			SELECT count(*) FROM ldb.main.records
			WHERE log_position = 100
			  AND octet_length(record_id) = 16
			  AND stream = 'orders-1'
			  AND category = 'orders'
			  AND schema_name = 'OrderPlaced'
			  AND schema_id IS NULL
			  AND schema_format = 'Json'
			  AND content = $content
			  AND created_at = $created_at
			  AND embedding = CAST($embedding AS FLOAT[4])
			""";
		command.Parameters.Add(new("content", content));
		command.Parameters.Add(new("created_at", expectedCreatedAt));
		command.Parameters.Add(new("embedding", expectedEmbedding));

		await Assert.That(written).IsEqualTo(1);
		await Assert.That((long)command.ExecuteScalar()!).IsEqualTo(1L);
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.records")).IsEqualTo(1L);
		await Assert.That(CountManifests(dir.Path) - baseline).IsEqualTo(1);
	}

	[Test]
	public async ValueTask resumes_from_the_highest_committed_position(CancellationToken cancellationToken) {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var schema = new KontextRecordsSchema(pool, new() { Dimension = 4 });
		await schema.CreateAsync(cancellationToken);

		await using var connection = pool.Open();
		Exec(connection, "USE ldb");

		using var writer    = NewWriter(connection);
		var       timestamp = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

		// Act
		var beforeAnyWrite = schema.ReadLastPosition(connection);

		await writer.ProjectAsync([
			NewRecord(logPosition: 100, "orders-1", "OrderPlaced", """{"n": 1}""", isJson: true, timestamp),
			NewRecord(logPosition: 205, "orders-2", "OrderPlaced", """{"n": 2}""", isJson: true, timestamp)
		], cancellationToken);

		var afterWrite = schema.ReadLastPosition(connection);

		// Assert — empty table means no checkpoint; after a flush the checkpoint is the batch's max.
		await Assert.That(beforeAnyWrite).IsNull();
		await Assert.That(afterWrite).IsEqualTo(205L);
	}

	[Test]
	public async ValueTask extractor_failure_skips_the_record_and_keeps_indexing(CancellationToken cancellationToken) {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var schema = new KontextRecordsSchema(pool, new() { Dimension = 4 });
		await schema.CreateAsync(cancellationToken);

		await using var connection = pool.Open();
		Exec(connection, "USE ldb");

		using var writer = new KontextRecordsWriter(
			connection,
			PoisonExtractor,
			new FakeEmbeddingGenerator(),
			new EmbeddingGenerationOptions { Dimensions = 4 },
			NullLogger<KontextRecordsWriter>.Instance);

		var timestamp = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
		var poison    = NewRecord(logPosition: 100, "orders-1", "PoisonEvent", """{"bad": true}""", isJson: true, timestamp);
		var good      = NewRecord(logPosition: 101, "orders-1", "OrderPlaced", """{"n": 1}""", isJson: true, timestamp);

		// Act
		var written = await writer.ProjectAsync([poison, good], cancellationToken);

		// Assert — the poison record is counted and skipped, the good one lands, nothing stalls.
		await Assert.That(written).IsEqualTo(1);
		await Assert.That(writer.SkippedRecords).IsEqualTo(1L);
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.records WHERE log_position = 101")).IsEqualTo(1L);

		static string? PoisonExtractor(in ResolvedEvent record, string schemaFormat) =>
			record.Event.EventType == "PoisonEvent"
				? throw new InvalidOperationException("poison")
				: KontextRecordsContent.Json(in record, schemaFormat);
	}

	static ResolvedEvent NewRecord(long logPosition, string stream, string eventType, string data, bool isJson, DateTime timestamp) {
		var record = new EventRecord(
			eventNumber: 0,
			logPosition,
			correlationId: Guid.NewGuid(),
			eventId: Guid.NewGuid(),
			transactionPosition: logPosition,
			transactionOffset: 0,
			stream,
			expectedVersion: -1,
			timestamp,
			isJson ? PrepareFlags.Data | PrepareFlags.IsJson : PrepareFlags.Data,
			eventType,
			Encoding.UTF8.GetBytes(data),
			metadata: null);

		return ResolvedEvent.ForUnresolvedEvent(record, 0L);
	}

	static KontextRecordsWriter NewWriter(DuckDBAdvancedConnection connection) =>
		new(connection,
			KontextRecordsContent.Json,
			new FakeEmbeddingGenerator(),
			new EmbeddingGenerationOptions { Dimensions = 4 },
			NullLogger<KontextRecordsWriter>.Instance);

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

	static int CountManifests(string storagePath) =>
		Directory.GetFiles(storagePath, "*.manifest", SearchOption.AllDirectories).Length;

	static KontextConnectionPool NewPool(string dir) =>
		new($"Data Source={Path.Combine(dir, "engine.db")};access_mode=READ_WRITE", dir);

	/// <summary>
	/// Deterministic 4-dim embeddings: a unit vector on the axis picked by the content's length,
	/// so a test can recompute the exact vector the writer wrote and compare it in SQL.
	/// </summary>
	sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>> {
		public static float[] Embed(string content) {
			var vector = new float[4];
			vector[content.Length % 4] = 1f;
			return vector;
		}

		public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
			IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default
		) => Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(values.Select(value => new Embedding<float>(Embed(value))).ToList()));

		public object? GetService(Type serviceType, object? serviceKey = null) => null;

		public void Dispose() { }
	}

	/// <summary>A unique temp directory owned by one test; deleted on dispose.</summary>
	sealed class TempDir : IDisposable {
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kontext-records-writer", Guid.NewGuid().ToString("N"));

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
