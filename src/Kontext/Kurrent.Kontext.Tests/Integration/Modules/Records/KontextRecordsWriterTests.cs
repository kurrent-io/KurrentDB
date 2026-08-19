// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Modules.Records;
using Kurrent.Kontext.Modules.Records.Data;
using Kurrent.Kontext.Testing;
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
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		await KontextMigrations.CreateEngine(dataSources).EnsureAsync(cancellationToken);

		await using var connection = dataSources.OpenLanceWriter();

		using var writer = NewWriter(connection);

		var payload           = """{"total": 42}""";
		var expectedContent   = "total: 42"; // the flattened form JsonNormalizerTests pins
		var timestamp         = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
		var expectedCreatedAt = new DateTimeOffset(timestamp).ToUnixTimeMilliseconds();
		var expectedEmbedding = await KontextTestEmbeddings.Embed(expectedContent, cancellationToken);

		var json = CreateRecord(logPosition: 100, "orders-1", "OrderPlaced", payload, SchemaDataFormat.Json, timestamp, schemaId: "urn:schemas:orders:OrderPlaced:1");

		var baseline = CountManifests(dir.Path, "records.lance");

		// Act
		var written = await writer.ProjectAsync([json], cancellationToken);

		// Assert — the record lands whole: every column, schema id from its header, the payload
		// verbatim in data and flattened in content, one flush = one lance commit.
		using var command = connection.CreateCommand();
		command.CommandText =
			$$"""
			SELECT count(*) FROM ldb.main.records
			WHERE log_position = 100
			  AND octet_length(record_id) = 16
			  AND stream = 'orders-1'
			  AND category = 'orders'
			  AND schema_name = 'OrderPlaced'
			  AND schema_id = 'urn:schemas:orders:OrderPlaced:1'
			  AND schema_format = 'Json'
			  AND data = $data
			  AND content = $content
			  AND created_at = $created_at
			  AND embedding = CAST($embedding AS FLOAT[{{KontextIndexConstants.VectorsDimension}}])
			""";
		command.Parameters.Add(new("data", payload));
		command.Parameters.Add(new("content", expectedContent));
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
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		await KontextMigrations.CreateEngine(dataSources).EnsureAsync(cancellationToken);

		await using var connection = dataSources.OpenLanceWriter();

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
	public async ValueTask empty_payload_is_skipped_and_indexing_continues(CancellationToken cancellationToken) {
		// Arrange
		using var dir         = new TempDir();
		using var dataSources = NewDataSources(dir.Path);

		await KontextMigrations.CreateEngine(dataSources).EnsureAsync(cancellationToken);

		await using var connection = dataSources.OpenLanceWriter();

		using var writer = NewWriter(connection);

		var timestamp = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
		var empty     = CreateRecord(logPosition: 100, "orders-1", "OrderVoided", "", SchemaDataFormat.Json, timestamp);
		var good      = CreateRecord(logPosition: 101, "orders-1", "OrderPlaced", """{"n": 1}""", SchemaDataFormat.Json, timestamp);

		// Act
		var written = await writer.ProjectAsync([empty, good], cancellationToken);

		// Assert — the empty record is counted and skipped, the good one lands, nothing stalls.
		await Assert.That(written).IsEqualTo(1);
		await Assert.That(writer.SkippedRecords).IsEqualTo(1L);
		await Assert.That(Scalar(connection, "SELECT count(*) FROM ldb.main.records WHERE log_position = 101")).IsEqualTo(1L);
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
			KontextTestEmbeddings.Model,
			KontextTestEmbeddings.Options,
			NullLogger<KontextRecordsWriter>.Instance);

	static long Scalar(DuckDBAdvancedConnection connection, string sql) {
		using var command = connection.CreateCommand();
		command.CommandText = sql;
		return (long)command.ExecuteScalar()!;
	}

	static int CountManifests(string storagePath, string dataset) =>
		Directory.GetFiles(Path.Combine(storagePath, dataset), "*.manifest", SearchOption.AllDirectories).Length;

	static KontextDataSource NewDataSources(string dir) => MemorySeeding.NewDataSources(dir);

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
