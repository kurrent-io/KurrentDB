// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.Indexes.Default;
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
		string cat1 = "first";
		string cat2 = "second";

		string cat1_stream1 = $"{cat1}-{Guid.NewGuid()}";
		string cat1_stream2 = $"{cat1}-{Guid.NewGuid()}";

		string cat2_stream1 = $"{cat2}-{Guid.NewGuid()}";

		string cat1_et1 = $"{cat1}-{Guid.NewGuid()}";
		string cat1_et2 = $"{cat1}-{Guid.NewGuid()}";
		string cat1_et3 = $"{cat1}-{Guid.NewGuid()}";

		string cat2_et1 = $"{cat2}-{Guid.NewGuid()}";
		string cat2_et2 = $"{cat2}-{Guid.NewGuid()}";

		ResolvedEvent[] events = [
			From(cat1_stream1, 0, 100, cat1_et1, []),
			From(cat2_stream1, 0, 110, cat2_et1, []),
			From(cat1_stream1, 1, 117, cat1_et2, []),
			From(cat1_stream1, 2, 200, cat1_et3, []),
			From(cat1_stream2, 0, 213, cat1_et1, []),
			From(cat2_stream1, 0, 394, cat2_et2, []),
			From(cat1_stream2, 1, 500, cat1_et2, []),
			From(cat1_stream1, 3, 601, cat1_et3, []),
			From(cat1_stream1, 4, 987, cat1_et1, [])
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
			new ReferenceRecord(0, cat1_et1),
			new ReferenceRecord(1, cat2_et1),
			new ReferenceRecord(2, cat1_et2),
			new ReferenceRecord(3, cat1_et3),
			new ReferenceRecord(4, cat2_et2)
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

		AssertGetStreamIdByNameQueryReturns(cat1_stream1, 0);
		AssertGetStreamIdByNameQueryReturns(cat2_stream1, 1);
		AssertGetStreamIdByNameQueryReturns(cat1_stream2, 2);
	}

	[Fact]
	public void UncommittedMultipleEventsInMultipleStreams_AreNOTIndexedCorrectly() {
		// Given
		string cat1 = "first";
		string cat2 = "second";

		string cat1_stream1 = $"{cat1}-{Guid.NewGuid()}";
		string cat1_stream2 = $"{cat1}-{Guid.NewGuid()}";

		string cat2_stream1 = $"{cat2}-{Guid.NewGuid()}";

		string cat1_et1 = $"{cat1}-{Guid.NewGuid()}";
		string cat1_et2 = $"{cat1}-{Guid.NewGuid()}";
		string cat1_et3 = $"{cat1}-{Guid.NewGuid()}";

		string cat2_et1 = $"{cat2}-{Guid.NewGuid()}";
		string cat2_et2 = $"{cat2}-{Guid.NewGuid()}";

		ResolvedEvent[] events = [
			From(cat1_stream1, 0, 100, cat1_et1, []),
			From(cat2_stream1, 0, 110, cat2_et1, []),
			From(cat1_stream1, 1, 117, cat1_et2, []),
			From(cat1_stream1, 2, 200, cat1_et3, []),
			From(cat1_stream2, 0, 213, cat1_et1, []),
			From(cat2_stream1, 0, 394, cat2_et2, []),
			From(cat1_stream2, 1, 500, cat1_et2, []),
			From(cat1_stream1, 3, 601, cat1_et3, []),
			From(cat1_stream1, 4, 987, cat1_et1, [])
		];

		// When
		foreach (var resolvedEvent in events) {
			_processor.Index(resolvedEvent);
		}

		// Then
		AssertLastSequenceQueryReturns(-1);
		AssertLastLogPositionQueryReturns(-1);

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
			new ReferenceRecord(0, cat1_et1),
			new ReferenceRecord(1, cat2_et1),
			new ReferenceRecord(2, cat1_et2),
			new ReferenceRecord(3, cat1_et3),
			new ReferenceRecord(4, cat2_et2)
		]);
		AssertGetEventTypeMaxSequencesQueryReturns([]);
		AssertReadEventTypeIndexQueryReturns(0, []);
		AssertReadEventTypeIndexQueryReturns(1, []);
		AssertReadEventTypeIndexQueryReturns(2, []);
		AssertReadEventTypeIndexQueryReturns(3, []);
		AssertReadEventTypeIndexQueryReturns(4, []);

		// Streams
		AssertGetStreamMaxSequencesQueryReturns(-1);

		AssertGetStreamIdByNameQueryReturns(cat1_stream1, null);
		AssertGetStreamIdByNameQueryReturns(cat2_stream1, null);
		AssertGetStreamIdByNameQueryReturns(cat1_stream2, null);
	}

	private void AssertDefaultIndexQueryReturns(List<AllRecord> expected) {
		var records = DuckDb.Pool.Query<(long, long), AllRecord, DefaultSql.DefaultIndexQuery>((0, 32));

		Assert.Equal(expected, records);
	}

	private void AssertLastSequenceQueryReturns(long expectedLastSequence) {
		var actual = DuckDb.Pool.QueryFirstOrDefault<long, DefaultSql.GetLastSequenceSql>();

		Assert.Equal(expectedLastSequence, actual);
	}

	private void AssertLastLogPositionQueryReturns(long expectedLastSequence) {
		var actual = DuckDb.Pool.QueryFirstOrDefault<long, DefaultSql.GetLastLogPositionSql>();

		Assert.Equal(expectedLastSequence, actual);
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
		var actual = DuckDb.Pool.QueryFirstOrDefault<GetStreamIdByNameQueryArgs, long, GetStreamIdByNameQuery>(
			new GetStreamIdByNameQueryArgs(streamName)
		);

		Assert.Equal(expectedId, actual);
	}

	private void AssertGetStreamMaxSequencesQueryReturns(long expectedId) {
		var actual = DuckDb.Pool.QueryFirstOrDefault<long, GetStreamMaxSequencesQuery>();

		Assert.Equal(expectedId, actual);
	}

	private readonly DefaultIndexProcessor _processor;
	private readonly DefaultIndex _defaultIndex;

	public DefaultIndexProcessorTests() {
		var reader = new DummyReadIndex();

		const int commitBatchSize = 9;
		_defaultIndex = new DefaultIndex(DuckDb, reader, commitBatchSize);
		_processor = new DefaultIndexProcessor(DuckDb, _defaultIndex, commitBatchSize);
	}

	public override Task DisposeAsync() {
		_defaultIndex.Dispose();
		return base.DisposeAsync();
	}
}
