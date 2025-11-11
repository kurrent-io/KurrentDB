// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Dapper;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.Data;
using KurrentDB.Core.Index.Hashes;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.SecondaryIndexing.Indexes;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Storage;
using KurrentDB.SecondaryIndexing.Tests.Fakes;
using KurrentDB.SecondaryIndexing.Tests.Fixtures;
using static KurrentDB.SecondaryIndexing.Indexes.Category.CategorySql;
using static KurrentDB.SecondaryIndexing.Indexes.Default.DefaultSql;
using static KurrentDB.SecondaryIndexing.Indexes.EventType.EventTypeSql;
using static KurrentDB.SecondaryIndexing.Tests.Fakes.TestResolvedEventFactory;

namespace KurrentDB.SecondaryIndexing.Tests.Indexes;

public class DefaultIndexProcessorTests : DuckDbIntegrationTest<DefaultIndexProcessorTests> {
	[Fact]
	public void WhenNoEventsProcessedYet_HasDefaultValues() {
		Assert.Equal(-1, _processor.LastIndexedPosition.PreparePosition);
	}

	[Fact]
	public void CommittedMultipleEventsInMultipleStreams_AreIndexed() {
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
			From(cat1Stream1, 0, 100, cat1Et1, []), // 0
			From(cat2Stream1, 0, 110, cat2Et1, []), // 1
			From(cat1Stream1, 1, 117, cat1Et2, []), // 2
			From(cat1Stream1, 2, 200, cat1Et3, []), // 3
			From(cat1Stream2, 0, 213, cat1Et1, []), // 4
			From(cat2Stream1, 0, 394, cat2Et2, []), // 5
			From(cat1Stream2, 1, 500, cat1Et2, []), // 6
			From(cat1Stream1, 3, 601, cat1Et3, []), // 7
			From(cat1Stream1, 4, 987, cat1Et1, []) // 8
		];

		// When
		foreach (var resolvedEvent in events) {
			_processor.TryIndex(resolvedEvent);
		}

		_processor.Commit();

		// Then
		// Default Index
		AssertLastLogPositionQueryReturns(987);

		AssertDefaultIndexQueryReturns([100, 110, 117, 200, 213, 394, 500, 601, 987]);

		AssertCategoryIndexQueryReturns(cat1, [100, 117, 200, 213, 500, 601, 987]);
		AssertCategoryIndexQueryReturns(cat2, [110, 394]);

		AssertReadEventTypeIndexQueryReturns(cat1Et1, [100, 213, 987]);
		AssertReadEventTypeIndexQueryReturns(cat2Et1, [110]);
		AssertReadEventTypeIndexQueryReturns(cat1Et2, [117, 500]);
		AssertReadEventTypeIndexQueryReturns(cat1Et3, [200, 601]);
		AssertReadEventTypeIndexQueryReturns(cat2Et2, [394]);
	}

	[Fact]
	public void CommittedMultipleEventsToAStringWithoutCategory_AreIndexed() {
		// Given
		const string streamName = "hello";
		string eventType1 = $"{Guid.NewGuid()}";
		string eventType2 = $"{Guid.NewGuid()}";
		string eventType3 = $"{Guid.NewGuid()}";

		ResolvedEvent[] events = [
			From(streamName, 0, 100, eventType1, []),
			From(streamName, 1, 117, eventType2, []),
			From(streamName, 2, 200, eventType3, []),
		];

		// When
		foreach (var resolvedEvent in events) {
			_processor.TryIndex(resolvedEvent);
		}

		_processor.Commit();

		// Then
		// Default Index
		AssertLastLogPositionQueryReturns(200);

		AssertDefaultIndexQueryReturns([100, 117, 200]);

		// Categories
		AssertGetCategoriesQueryReturns(["hello"]);
		AssertCategoryIndexQueryReturns(streamName, [100, 117, 200]);

		// EventTypes
		AssertGetAllEventTypesQueryReturns([eventType1, eventType2, eventType3]);
		AssertReadEventTypeIndexQueryReturns(eventType1, [100]);
		AssertReadEventTypeIndexQueryReturns(eventType2, [117]);
		AssertReadEventTypeIndexQueryReturns(eventType3, [200]);
	}

	private void AssertDefaultIndexQueryReturns(List<long> expected) {
		var records =
			DuckDb.QueryToList<ReadDefaultIndexQueryArgs, IndexQueryRecord, ReadDefaultIndexQueryExcl>(new(-1, long.MaxValue,
				int.MaxValue));

		Assert.Equal(expected, records.Select(x => x.LogPosition));
	}

	private void AssertLastLogPositionQueryReturns(long? expectedLogPosition) {
		var actual = DuckDb.QueryFirstOrDefault<LastPositionResult, GetLastLogPositionQuery>();

		Assert.Equal(expectedLogPosition, actual?.PreparePosition);
	}

	private void AssertGetCategoriesQueryReturns(string[] expected) {
		using var connection = DuckDb.Open();
		var records = connection.Query<string>("select distinct category from idx_all order by log_position");

		Assert.Equal(expected, records);
	}

	private void AssertCategoryIndexQueryReturns(string category, List<long> expected) {
		var records =
			DuckDb.QueryToList<CategoryIndexQueryArgs, IndexQueryRecord, CategoryIndexQueryIncl>(new(category, 0, long.MaxValue, 32));

		Assert.Equal(expected, records.Select(x => x.LogPosition));
	}

	private void AssertGetAllEventTypesQueryReturns(string[] expected) {
		using var connection = DuckDb.Open();
		var records = connection.Query<string>("select distinct event_type from idx_all order by log_position");

		Assert.Equal(expected, records);
	}

	private void AssertReadEventTypeIndexQueryReturns(string eventType, List<long> expected) {
		var records = DuckDb.QueryToList<ReadEventTypeIndexQueryArgs, IndexQueryRecord, ReadEventTypeIndexQueryIncl>(
			new(eventType, 0, long.MaxValue, 32)
		);

		Assert.Equal(expected, records.Select(x => x.LogPosition));
	}

	readonly DefaultIndexProcessor _processor;

	public DefaultIndexProcessorTests() {
		ReadIndexStub.Build();

		const int commitBatchSize = 9;
		var hasher = new CompositeHasher<string>(new XXHashUnsafe(), new Murmur3AUnsafe());
		var inflightRecordsCache = new IndexInFlightRecords(new() { CommitBatchSize = commitBatchSize });

		var publisher = new FakePublisher();

		_processor = new(DuckDb, inflightRecordsCache, publisher, hasher, new("test"));
	}

	public override ValueTask DisposeAsync() {
		_processor.Dispose();
		return base.DisposeAsync();
	}
}
