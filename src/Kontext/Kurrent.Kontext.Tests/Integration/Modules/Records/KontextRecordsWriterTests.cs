// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Modules.Records;
using Kurrent.Kontext.Modules.Records.Data;
using Kurrent.Quack;
using Kurrent.Surge;
using Kurrent.Surge.Schema;
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

		await using var connection = pool.OpenLanceWriter();

		using var writer = NewWriter(connection);

		var content           = """{"total": 42}""";
		var timestamp         = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
		var expectedCreatedAt = new DateTimeOffset(timestamp).ToUnixTimeMilliseconds();
		var expectedEmbedding = FakeEmbeddingGenerator.Embed(content);

		var json    = CreateRecord(logPosition: 100, "orders-1", "OrderPlaced", content, SchemaDataFormat.Json, timestamp, schemaId: "urn:schemas:orders:OrderPlaced:1");
		var bytes   = CreateRecord(logPosition: 101, "orders-1", "OrderSnapshot", "raw-bytes", SchemaDataFormat.Bytes, timestamp);
		var control = CreateRecord(logPosition: 102, "orders-1", "$subscription-caughtUp", "{}", SchemaDataFormat.Json, timestamp);

		var baseline = CountManifests(dir.Path, "records.lance");

		// Act
		var written = await writer.ProjectAsync([json, bytes, control], cancellationToken);

		// Assert — the JSON record lands whole (every column, schema id from its header), the
		// undecodable record and the control record never land, one flush = one lance commit.
		using var command = connection.CreateCommand();
		command.CommandText =
			"""
			SELECT count(*) FROM ldb.main.records
			WHERE log_position = 100
			  AND octet_length(record_id) = 16
			  AND stream = 'orders-1'
			  AND category = 'orders'
			  AND schema_name = 'OrderPlaced'
			  AND schema_id = 'urn:schemas:orders:OrderPlaced:1'
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
		await Assert.That(CountManifests(dir.Path, "records.lance") - baseline).IsEqualTo(1);
	}

	[Test]
	public async ValueTask batch_and_checkpoint_commit_and_revert_together(CancellationToken cancellationToken) {
		// Arrange — the indexer's exact loop shape: writer flush + checkpoint MERGE in one
		// transaction on the lance-redirected connection.
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var schema = new KontextRecordsSchema(pool, new() { Dimension = 4 });
		await schema.CreateAsync(cancellationToken);

		await using var connection = pool.OpenLanceWriter();

		var checkpoints = new KontextCheckpointStore("records-writer-test");
		checkpoints.EnsureSchema(connection);

		using var writer    = NewWriter(connection);
		var       timestamp = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

		// Act — committed batch.
		using (var tx = connection.BeginTransaction()) {
			await writer.ProjectAsync([CreateRecord(logPosition: 100, "orders-1", "OrderPlaced", """{"n": 1}""", SchemaDataFormat.Json, timestamp)], cancellationToken);
			checkpoints.Store(connection, RecordPosition.ForLog(100));
			tx.CommitOnDispose();
		}

		var afterCommit = checkpoints.Load(connection);

		// Act — rolled-back batch: dispose without commit.
		using (connection.BeginTransaction()) {
			await writer.ProjectAsync([CreateRecord(logPosition: 205, "orders-2", "OrderPlaced", """{"n": 2}""", SchemaDataFormat.Json, timestamp)], cancellationToken);
			checkpoints.Store(connection, RecordPosition.ForLog(205));
		}

		var afterRollback = checkpoints.Load(connection);

		// Assert — data and checkpoint advanced together, then reverted together.
		await Assert.That((ulong?)afterCommit).IsEqualTo(100UL);
		await Assert.That((ulong?)afterRollback).IsEqualTo(100UL);
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.records WHERE log_position = 100")).IsEqualTo(1L);
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.records WHERE log_position = 205")).IsEqualTo(0L);
	}

	[Test]
	public async ValueTask extractor_failure_skips_the_record_and_keeps_indexing(CancellationToken cancellationToken) {
		// Arrange
		using var dir  = new TempDir();
		using var pool = NewPool(dir.Path);

		var schema = new KontextRecordsSchema(pool, new() { Dimension = 4 });
		await schema.CreateAsync(cancellationToken);

		await using var connection = pool.OpenLanceWriter();

		using var writer = new KontextRecordsWriter(
			connection,
			PoisonExtractor,
			new FakeEmbeddingGenerator(),
			new EmbeddingGenerationOptions { Dimensions = 4 },
			NullLogger<KontextRecordsWriter>.Instance);

		var timestamp = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
		var poison    = CreateRecord(logPosition: 100, "orders-1", "PoisonEvent", """{"bad": true}""", SchemaDataFormat.Json, timestamp);
		var good      = CreateRecord(logPosition: 101, "orders-1", "OrderPlaced", """{"n": 1}""", SchemaDataFormat.Json, timestamp);

		// Act
		var written = await writer.ProjectAsync([poison, good], cancellationToken);

		// Assert — the poison record is counted and skipped, the good one lands, nothing stalls.
		await Assert.That(written).IsEqualTo(1);
		await Assert.That(writer.SkippedRecords).IsEqualTo(1L);
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.records WHERE log_position = 101")).IsEqualTo(1L);

		static string? PoisonExtractor(SurgeRecord record) =>
			record.SchemaInfo.SchemaName == "PoisonEvent"
				? throw new InvalidOperationException("poison")
				: KontextRecordsContent.Json(record);
	}

	// The same shape as the memories tests' CreateRecord: the writer reads Position, SchemaInfo,
	// Headers, Data, and Timestamp — Value stays null exactly as SkipDecoding leaves it.
	static SurgeRecord CreateRecord(
		long logPosition, string stream, string schemaName, string data,
		SchemaDataFormat format, DateTime timestamp, string? schemaId = null
	) {
		var headers = new Headers();

		if (schemaId is not null)
			headers[HeaderKeys.SchemaId] = schemaId;

		return new() {
			Id         = Guid.NewGuid(),
			Position   = RecordPosition.ForStream(StreamId.From(stream), StreamRevision.From(0), LogPosition.From(logPosition, logPosition)),
			Timestamp  = timestamp,
			SchemaInfo = new SchemaInfo(schemaName, format),
			Data       = Encoding.UTF8.GetBytes(data),
			Value      = null!,
			ValueType  = typeof(object),
			SequenceId = (ulong)logPosition,
			Headers    = headers
		};
	}

	static KontextRecordsWriter NewWriter(DuckDBAdvancedConnection connection) =>
		new(connection,
			KontextRecordsContent.Json,
			new FakeEmbeddingGenerator(),
			new EmbeddingGenerationOptions { Dimensions = 4 },
			NullLogger<KontextRecordsWriter>.Instance);

	static long Scalar(DuckDBAdvancedConnection connection, string sql) {
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return (long)command.ExecuteScalar()!;
	}

	static int CountManifests(string storagePath, string dataset) =>
		Directory.GetFiles(Path.Combine(storagePath, dataset), "*.manifest", SearchOption.AllDirectories).Length;

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
