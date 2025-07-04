// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using DuckDB.NET.Data;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Data;
using KurrentDB.Core.Index.Hashes;
using KurrentDB.Core.Services;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Indexes.Diagnostics;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Indexes.Stream;
using KurrentDB.SecondaryIndexing.Storage;
using KurrentDB.SecondaryIndexing.Tests.Fakes;
using KurrentDB.SecondaryIndexing.Tests.Fixtures;
using static KurrentDB.SecondaryIndexing.Tests.Fakes.TestResolvedEventFactory;
using static KurrentDB.SecondaryIndexing.Indexes.Category.CategorySql;
using static KurrentDB.SecondaryIndexing.Indexes.EventType.EventTypeSql;
using static KurrentDB.SecondaryIndexing.Indexes.Stream.StreamSql;

namespace KurrentDB.SecondaryIndexing.Tests.Indexes;

public class DefaultIndexProcessorTests : DuckDbIntegrationTest {
	[Fact]
	public void CommittedMultipleEventsInMultipleStreams_AreIndexedCorrectly() {
		// Given
		const string cat1 = "first";
		const string cat2 = "second";

		string cat1Stream1 = $"{cat1}-{Guid.NewGuid()}";
		string cat1Stream2 = $"{cat1}-{Guid.NewGuid()}";

		string cat2Stream1 = $"{cat2}-{Guid.NewGuid()}";

		string cat1Et1 = $"{cat1}-{Guid.NewGuid()}";
		string cat1Et2 = $"{cat1}-{Guid.NewGuid()}";
		string cat1Et3 = $"{cat1}-{Guid.NewGuid()}";

		string cat2Et1 = $"{cat2}-{Guid.NewGuid()}";
		string cat2Et2 = $"{cat2}-{Guid.NewGuid()}";

		ResolvedEvent[] events = [
			From(cat1Stream1, 0, 100, cat1Et1, []),
			From(cat2Stream1, 0, 110, cat2Et1, []),
			From(cat1Stream1, 1, 117, cat1Et2, []),
			From(cat1Stream1, 2, 200, cat1Et3, []),
			From(cat1Stream2, 0, 213, cat1Et1, []),
			From(cat2Stream1, 0, 394, cat2Et2, []),
			From(cat1Stream2, 1, 500, cat1Et2, []),
			From(cat1Stream1, 3, 601, cat1Et3, []),
			From(cat1Stream1, 4, 987, cat1Et1, [])
		];

		// When
		foreach (var resolvedEvent in events) {
			_processor.Index(resolvedEvent);
		}

		_processor.Commit();

		// Then
		// Default Index
		AssertLastSequenceQueryReturns(8);
		AssertLastLogPositionQueryReturns(987);

		AssertDefaultIndexQueryReturns([
			new AllRecord(0, 100),
			new AllRecord(1, 110),
			new AllRecord(2, 117),
			new AllRecord(3, 200),
			new AllRecord(4, 213),
			new AllRecord(5, 394),
			new AllRecord(6, 500),
			new AllRecord(7, 601),
			new AllRecord(8, 987)
		]);

		// Categories
		AssertGetCategoriesQueryReturns([
			new ReferenceRecord(0, cat1),
			new ReferenceRecord(1, cat2)
		]);
		AssertGetCategoriesMaxSequencesQueryReturns([
			(0, 6),
			(1, 1)
		]);
		AssertCategoryIndexQueryReturns(0, [
			new CategoryRecord(0, 100),
			new CategoryRecord(1, 117),
			new CategoryRecord(2, 200),
			new CategoryRecord(3, 213),
			new CategoryRecord(4, 500),
			new CategoryRecord(5, 601),
			new CategoryRecord(6, 987)
		]);
		AssertCategoryIndexQueryReturns(1, [
			new CategoryRecord(0, 110),
			new CategoryRecord(1, 394)
		]);

		// EventTypes
		AssertGetAllEventTypesQueryReturns([
			new ReferenceRecord(0, cat1Et1),
			new ReferenceRecord(1, cat2Et1),
			new ReferenceRecord(2, cat1Et2),
			new ReferenceRecord(3, cat1Et3),
			new ReferenceRecord(4, cat2Et2)
		]);
		AssertGetEventTypeMaxSequencesQueryReturns([
			(0, 2),
			(1, 0),
			(2, 1),
			(3, 1),
			(4, 0)
		]);
		AssertReadEventTypeIndexQueryReturns(0, [
			new EventTypeRecord(0, 100),
			new EventTypeRecord(1, 213),
			new EventTypeRecord(2, 987)
		]);
		AssertReadEventTypeIndexQueryReturns(1, [
			new EventTypeRecord(0, 110)
		]);
		AssertReadEventTypeIndexQueryReturns(2, [
			new EventTypeRecord(0, 117),
			new EventTypeRecord(1, 500)
		]);
		AssertReadEventTypeIndexQueryReturns(3, [
			new EventTypeRecord(0, 200),
			new EventTypeRecord(1, 601)
		]);
		AssertReadEventTypeIndexQueryReturns(4, [
			new EventTypeRecord(0, 394)
		]);

		// Streams
		AssertGetStreamMaxSequencesQueryReturns(2);

		AssertGetStreamIdByNameQueryReturns(cat1Stream1, 0);
		AssertGetStreamIdByNameQueryReturns(cat2Stream1, 1);
		AssertGetStreamIdByNameQueryReturns(cat1Stream2, 2);
	}

	[Fact]
	public void UncommittedMultipleEventsInMultipleStreams_AreNOTIndexedCorrectly() {
		// Given
		const string cat1 = "first";
		const string cat2 = "second";

		var cat1Stream1 = $"{cat1}-{Guid.NewGuid()}";
		var cat1Stream2 = $"{cat1}-{Guid.NewGuid()}";

		var cat2Stream1 = $"{cat2}-{Guid.NewGuid()}";

		var cat1Et1 = $"{cat1}-{Guid.NewGuid()}";
		var cat1Et2 = $"{cat1}-{Guid.NewGuid()}";
		var cat1Et3 = $"{cat1}-{Guid.NewGuid()}";

		var cat2Et1 = $"{cat2}-{Guid.NewGuid()}";
		var cat2Et2 = $"{cat2}-{Guid.NewGuid()}";

		ResolvedEvent[] events = [
			From(cat1Stream1, 0, 100, cat1Et1, []),
			From(cat2Stream1, 0, 110, cat2Et1, []),
			From(cat1Stream1, 1, 117, cat1Et2, []),
			From(cat1Stream1, 2, 200, cat1Et3, []),
			From(cat1Stream2, 0, 213, cat1Et1, []),
			From(cat2Stream1, 0, 394, cat2Et2, []),
			From(cat1Stream2, 1, 500, cat1Et2, []),
			From(cat1Stream1, 3, 601, cat1Et3, []),
			From(cat1Stream1, 4, 987, cat1Et1, [])
		];

		// When
		foreach (var resolvedEvent in events) {
			_processor.Index(resolvedEvent);
		}

		// Then
		AssertLastSequenceQueryReturns(null);
		AssertLastLogPositionQueryReturns(null);

		AssertDefaultIndexQueryReturns([]);

		// Categories
		// Note: Categories are inserted using separate connection
		AssertGetCategoriesQueryReturns([
			new ReferenceRecord(0, cat1),
			new ReferenceRecord(1, cat2)
		]);
		AssertGetCategoriesMaxSequencesQueryReturns([]);
		AssertCategoryIndexQueryReturns(0, []);
		AssertCategoryIndexQueryReturns(1, []);

		// EventTypes
		// Note: Event Types are inserted using separate connection
		AssertGetAllEventTypesQueryReturns([
			new ReferenceRecord(0, cat1Et1),
			new ReferenceRecord(1, cat2Et1),
			new ReferenceRecord(2, cat1Et2),
			new ReferenceRecord(3, cat1Et3),
			new ReferenceRecord(4, cat2Et2)
		]);
		AssertGetEventTypeMaxSequencesQueryReturns([]);
		AssertReadEventTypeIndexQueryReturns(0, []);
		AssertReadEventTypeIndexQueryReturns(1, []);
		AssertReadEventTypeIndexQueryReturns(2, []);
		AssertReadEventTypeIndexQueryReturns(3, []);
		AssertReadEventTypeIndexQueryReturns(4, []);

		// Streams
		AssertGetStreamMaxSequencesQueryReturns(null);

		AssertGetStreamIdByNameQueryReturns(cat1Stream1, null);
		AssertGetStreamIdByNameQueryReturns(cat2Stream1, null);
		AssertGetStreamIdByNameQueryReturns(cat1Stream2, null);
	}

	[Fact]
	public void StreamMetadataChangeWithEmptyValues_IsStored() {
		// Given
		const string cat1 = "first";
		string cat1Stream1 = $"{cat1}-{Guid.NewGuid()}";
		string cat1Et1 = $"{cat1}-{Guid.NewGuid()}";

		_processor.Index(From(cat1Stream1, 0, 100, cat1Et1, []));
		_processor.Commit();

		// When
		var streamMetadata = new StreamMetadata();

		_processor.HandleStreamMetadataChange(
			StreamMetadataChanged(cat1Stream1, 0, 1293, streamMetadata)
		);

		// Then
		var summary = DuckDb.Pool.GetStreamsSummary(cat1Stream1);

		Assert.NotNull(summary);
		Assert.Equal(cat1Stream1, summary.Value.Name);
		Assert.Null(summary.Value.MaxAge);
		Assert.Null(summary.Value.MaxCount);
		Assert.False(summary.Value.IsDeleted);
		Assert.Null(summary.Value.TruncateBefore);
		Assert.Null(summary.Value.Acl);
	}

	private static ResolvedEvent StreamMetadataChanged(
		string streamName,
		int streamPosition,
		long logPosition,
		StreamMetadata streamMetadata) =>
		From(
			SystemStreams.MetastreamOf(streamName),
			streamPosition,
			logPosition,
			SystemEventTypes.StreamMetadata,
			streamMetadata.ToJsonBytes()
		);

	private void AssertDefaultIndexQueryReturns(List<AllRecord> expected) {
		var records = DuckDb.Pool.Query<(long, long), AllRecord, DefaultSql.DefaultIndexQuery>((0, 32));

		Assert.Equal(expected, records);
	}

	private void AssertLastSequenceQueryReturns(long? expectedLastSequence) {
		var actual = DuckDb.Pool.QueryFirstOrDefault<Optional<long>, DefaultSql.GetLastSequenceSql>();

		Assert.Equal(expectedLastSequence, actual?.OrNull());
	}

	private void AssertLastLogPositionQueryReturns(long? expectedLastSequence) {
		var actual = DuckDb.Pool.QueryFirstOrDefault<Optional<long>, DefaultSql.GetLastLogPositionSql>();

		Assert.Equal(expectedLastSequence, actual?.OrNull());
	}

	private void AssertGetCategoriesQueryReturns(List<ReferenceRecord> expected) {
		var records = DuckDb.Pool.Query<ReferenceRecord, GetCategoriesQuery>().OrderBy(x => x.Id);

		Assert.Equal(expected, records);
	}

	private void AssertGetCategoriesMaxSequencesQueryReturns(List<(int Id, long Sequence)> expected) {
		var records = DuckDb.Pool.Query<(int Id, long Sequence), GetCategoriesMaxSequencesQuery>().OrderBy(x => x.Id);

		Assert.Equal(expected, records);
	}

	private void AssertCategoryIndexQueryReturns(long categoryId, List<CategoryRecord> expected) {
		var records = DuckDb.Pool
			.Query<CategoryIndexQueryArgs, CategoryRecord, CategoryIndexQuery>(
				new CategoryIndexQueryArgs((int)categoryId, 0, 32)
			);

		Assert.Equal(expected, records);
	}

	private void AssertGetAllEventTypesQueryReturns(List<ReferenceRecord> expected) {
		var records = DuckDb.Pool.Query<ReferenceRecord, GetAllEventTypesQuery>().OrderBy(x => x.Id);

		Assert.Equal(expected, records);
	}

	private void AssertGetEventTypeMaxSequencesQueryReturns(List<(int Id, long Sequence)> expected) {
		var records = DuckDb.Pool.Query<(int Id, long Sequence), GetEventTypeMaxSequencesQuery>().OrderBy(x => x.Id);

		Assert.Equal(expected, records);
	}

	private void AssertReadEventTypeIndexQueryReturns(long eventTypeId, List<EventTypeRecord> expected) {
		var records = DuckDb.Pool
			.Query<ReadEventTypeIndexQueryArgs, EventTypeRecord, ReadEventTypeIndexQuery>(
				new ReadEventTypeIndexQueryArgs((int)eventTypeId, 0, 32)
			);

		Assert.Equal(expected, records);
	}

	private void AssertGetStreamIdByNameQueryReturns(string streamName, long? expectedId) {
		var actual = DuckDb.Pool.GetStreamIdByName(streamName);

		Assert.Equal(expectedId, actual);
	}

	private void AssertGetStreamMaxSequencesQueryReturns(long? expectedId) {
		var actual = DuckDb.Pool.GetStreamMaxSequences();

		Assert.Equal(expectedId, actual);
	}

	private readonly DefaultIndexProcessor _processor;

	public DefaultIndexProcessorTests() {
		var reader = new DummyReadIndex();

		const int commitBatchSize = 9;
		var hasher = new CompositeHasher<string>(new XXHashUnsafe(), new Murmur3AUnsafe());
		var inflightRecordsCache = new DefaultIndexInFlightRecords(new() { CommitBatchSize = commitBatchSize });

		var publisher = new FakePublisher();

		var categoryIndexProcessor = new CategoryIndexProcessor(DuckDb, publisher);
		var eventTypeIndexProcessor = new EventTypeIndexProcessor(DuckDb, publisher);
		var streamIndexProcessor = new StreamIndexProcessor(DuckDb, reader.IndexReader.Backend, hasher);

		_processor = new DefaultIndexProcessor(
			DuckDb,
			inflightRecordsCache,
			categoryIndexProcessor,
			eventTypeIndexProcessor,
			streamIndexProcessor,
			new NoOpSecondaryIndexProgressTracker(),
			publisher
		);
	}

	public override Task DisposeAsync() {
		_processor.Dispose();
		return base.DisposeAsync();
	}
}

public class CleanUpTests {
	[Fact(Skip = "TODO: Check why is it failing")]
	public void DisposingAndDroppingDatabaseCleansAllResources() {
		var directory = Path.Combine(Path.GetTempPath(), "TestCleanup");

		if (!Directory.Exists(directory))
			Directory.CreateDirectory(directory);

		var fileName = Path.Combine(directory, Path.GetRandomFileName());
		var connectionString = $"Data Source={fileName};";
		var options = new DuckDbDataSourceOptions { ConnectionString = connectionString };

		using (var dataSource = new DuckDbDataSource(options)) {
			var reader = new DummyReadIndex();

			const int commitBatchSize = 9;
			var hasher = new CompositeHasher<string>(new XXHashUnsafe(), new Murmur3AUnsafe());
			var inflightRecordsCache = new DefaultIndexInFlightRecords(new() { CommitBatchSize = commitBatchSize });

			var publisher = new FakePublisher();

			var categoryIndexProcessor = new CategoryIndexProcessor(dataSource, publisher);
			var eventTypeIndexProcessor = new EventTypeIndexProcessor(dataSource, publisher);
			var streamIndexProcessor = new StreamIndexProcessor(dataSource, reader.IndexReader.Backend, hasher);

			using var processor = new DefaultIndexProcessor(
				dataSource,
				inflightRecordsCache,
				categoryIndexProcessor,
				eventTypeIndexProcessor,
				streamIndexProcessor,
				new NoOpSecondaryIndexProgressTracker(),
				publisher
			);

			const string cat1 = "first";

			var cat1Stream1 = $"{cat1}-{Guid.NewGuid()}";
			var cat1Et1 = $"{cat1}-{Guid.NewGuid()}";

			// When
			processor.Index(From(cat1Stream1, 0, 100, cat1Et1, []));
			processor.Commit();
		}

		Assert.True(DirectoryDeleter.TryForceDeleteDirectory(directory));

		Assert.False(Directory.Exists(directory));
		Assert.False(File.Exists(fileName));

		Directory.CreateDirectory(directory);

		using (var dataSource = new DuckDbDataSource(options)) {
			var actual = dataSource.Pool.QueryFirstOrDefault<Optional<long>, DefaultSql.GetLastSequenceSql>();

			Assert.Null(actual?.OrNull());
		}
	}


	[Fact]
	public void DisposingAndDroppingDatabaseCleansAllResourcesRawQuack() {
		var directory = Path.Combine(Path.GetTempPath(), "QuackDisposeTest");

		if (!Directory.Exists(directory))
			Directory.CreateDirectory(directory);

		var fileName = Path.Combine(directory, Path.GetRandomFileName());
		var connectionString = $"Data Source={fileName};";

		using (var pool = new DuckDBConnectionPool(connectionString)) {
			using (pool.Rent(out var connection)) {
				connection.ExecuteNonQuery<NotNullTableDefinition>();

				using (var appender = new Appender(connection, "test_table"u8)) {
					using (var row = appender.CreateRow()) {
						row.Append(1u);
						row.Append("test"u8);
					}
				}

				uint actualCount = 0U;

				foreach (ref readonly var row in connection.ExecuteQuery<(uint, string), QueryStatement>()) {
					Assert.Equal((1u, "test"), row);
					actualCount++;
				}

				Assert.Equal(1u, actualCount);
			}

			using (var c = new DuckDBConnection(connectionString)) {
				c.Open();
				c.Checkpoint();
			}
		}

		Assert.True(DirectoryDeleter.TryForceDeleteDirectory(directory));

		Assert.False(Directory.Exists(directory));
		Assert.False(File.Exists(fileName));

		Directory.CreateDirectory(directory);

		using (var pool = new DuckDBConnectionPool(connectionString)) {
			using (pool.Rent(out var connection)) {
				uint actualCount = 0U;
				connection.ExecuteNonQuery<NotNullTableDefinition>();

				foreach (ref readonly var row in connection.ExecuteQuery<(uint, string), QueryStatement>()) {
					Assert.NotEqual((1u, "test"), row);
					actualCount++;
				}

				Assert.Equal(0u, actualCount);
			}
		}
	}

	private struct NotNullTableDefinition : IParameterlessStatement {
		public static ReadOnlySpan<byte> CommandText => """
		                                                create table if not exists test_table (
		                                                    col0 UINTEGER not null primary key,
		                                                    col1 VARCHAR not null
		                                                );
		                                                """u8;
	}

	private struct QueryStatement : IQuery<(uint, string)> {
		public static ReadOnlySpan<byte> CommandText => "SELECT * FROM test_table;"u8;

		public static (uint, string) Parse(ref DataChunk.Row row) => (row.ReadUInt32(), row.ReadString());
	}
}
