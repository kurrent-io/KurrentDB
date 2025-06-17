// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Storage;
using KurrentDB.SecondaryIndexing.Tests.Fakes;
using KurrentDB.SecondaryIndexing.Tests.Fixtures;
using static KurrentDB.SecondaryIndexing.Tests.Fakes.TestResolvedEventFactory;
using static KurrentDB.SecondaryIndexing.Indexes.Category.CategorySql;

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
			From(cat1_stream1, 3, 601, cat1_et3, [])
		];

		// When
		foreach (var resolvedEvent in events) {
			_processor.Index(resolvedEvent);
		}

		_processor.Commit();

		// Then
		AssertLastSequenceQueryReturns(8);
		AssertLastLogPositionQueryReturns(601);

		AssertDefaultIndexQueryReturns([
			new AllRecord(1, 100),
			new AllRecord(2, 110),
			new AllRecord(3, 117),
			new AllRecord(4, 200),
			new AllRecord(5, 213),
			new AllRecord(6, 394),
			new AllRecord(7, 500),
			new AllRecord(8, 601)
		]);
		AssertCategoryIndexQueryReturns(1, [
			new CategoryRecord(0, 100),
			new CategoryRecord(1, 117),
			new CategoryRecord(2, 200),
			new CategoryRecord(3, 213),
			new CategoryRecord(4, 500),
			new CategoryRecord(5, 601)
		]);
		AssertCategoryIndexQueryReturns(2, [
			new CategoryRecord(0, 110),
			new CategoryRecord(1, 394)
		]);
	}

	[Fact(Skip = "¯\\_(ツ)_//¯")]
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
			From(cat1_stream1, 3, 601, cat1_et3, [])
		];

		// When
		// foreach (var resolvedEvent in events) {
		// 	_processor.Index(resolvedEvent);
		// }

		// Then
		AssertDefaultIndexQueryReturns([]);
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

	private void AssertCategoryIndexQueryReturns(long categoryId, List<CategoryRecord> expected) {
		var records = DuckDb.Pool
			.Query<CategoryIndexQueryArgs, CategoryRecord, CategoryIndexQuery>(
				new((int)categoryId, 0, 32)
			);

		Assert.Equal(expected, records);
	}

	private readonly DefaultIndexProcessor _processor;
	private readonly DefaultIndex _defaultIndex;

	public DefaultIndexProcessorTests() {
		var reader = new DummyReadIndex();
		_defaultIndex = new DefaultIndex(DuckDb, reader, 8);
		_processor = new DefaultIndexProcessor(DuckDb, _defaultIndex, 8);
	}

	public override Task DisposeAsync() {
		_defaultIndex.Dispose();
		return base.DisposeAsync();
	}
}
