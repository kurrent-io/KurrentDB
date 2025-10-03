// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.ClientPublisher;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Services.Transport.Common;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.SecondaryIndexing.Indexes;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Tests.Generators;
using KurrentDB.Surge.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

[Trait("Category", "Integration")]
public partial class IndexingTests(IndexingFixture fixture, ITestOutputHelper output)
	: ClusterVNodeTests<IndexingFixture>(fixture, output) {
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReadsAllEventsFromDefaultIndex(bool forwards) {
		Fixture.LogDatasetInfo();
		await ValidateRead(SystemStreams.DefaultSecondaryIndex, Fixture.AppendedBatches.ToDefaultIndexResolvedEvents(), forwards);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReadsAllEventsFromCategoryIndex(bool forwards) {
		foreach (var category in Fixture.Categories) {
			var expectedEvents = Fixture.AppendedBatches.ToCategoryIndexResolvedEvents(category);
			await ValidateRead(CategoryIndex.Name(category), expectedEvents, forwards);
		}
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ReadsAllEventsFromEventTypeIndex(bool forwards) {
		foreach (var eventType in Fixture.EventTypes) {
			var expectedEvents = Fixture.AppendedBatches.ToEventTypeIndexResolvedEvents(eventType);
			await ValidateRead(EventTypeIndex.Name(eventType), expectedEvents, forwards);
		}
	}

	[Fact]
	public async Task ReadFromUnknownIndexFails() {
		const string indexName = "$idx-dummy";
		var exception = await Assert.ThrowsAsync<ReadResponseException.IndexNotFound>(() => ValidateRead(indexName, [], true));
		Assert.Equal(indexName, exception.IndexName);
	}

	public enum CommitMode {
		None,
		CommitAndClear,
		CommitAndKeep,
	}

	private static readonly IEventFilter UserEventsFilter = new EventFilter.DefaultAllFilterStrategy.NonSystemStreamStrategy();

	[Theory]
	[InlineData(CommitMode.None)]
	[InlineData(CommitMode.CommitAndClear)]
	[InlineData(CommitMode.CommitAndKeep)]
	public async Task ReadFromBothDefault(CommitMode mode) {
		await ValidateRead(mode, SystemStreams.DefaultSecondaryIndex, UserEventsFilter);
	}

	[Theory]
	[InlineData(CommitMode.None)]
	[InlineData(CommitMode.CommitAndClear)]
	[InlineData(CommitMode.CommitAndKeep)]
	public async Task ReadFromBothCategory(CommitMode mode) {
		var category = Fixture.Categories.First();
		await ValidateRead(mode, CategoryIndex.Name(category), new CategoryFilter(category));
	}

	[Theory]
	[InlineData(CommitMode.None)]
	[InlineData(CommitMode.CommitAndClear)]
	[InlineData(CommitMode.CommitAndKeep)]
	public async Task ReadFromBothEventType(CommitMode mode) {
		var eventType = Fixture.EventTypes.First();
		await ValidateRead(mode, EventTypeIndex.Name(eventType), new EventTypeFilter(eventType));
	}

	private async Task ValidateRead(CommitMode mode, string indexName, IEventFilter filter) {
		var (records, firstPosition) = await SetupForRead(mode, filter);

		var resultBwd = await Fixture.Publisher.ReadIndex(indexName, Position.End, records.Length, forwards: false).ToListAsync();
		var actualBwd = resultBwd.Select(x => x.Event).ToArray();

		Assert.Equal(records, actualBwd);

		var resultFwd = await Fixture.Publisher.ReadIndex(indexName, firstPosition, long.MaxValue).ToListAsync();
		var actualFwd = resultFwd.Select(x => x.Event).ToArray();

		var expectedFwd = records.Reverse();
		Assert.Equal(expectedFwd, actualFwd);

	}

	private async Task<(EventRecord[] Records, Position FirstPosition)> SetupForRead(CommitMode mode, IEventFilter filter) {
		var logEvents = await Fixture.Publisher.ReadBackwards(Position.End, filter, Fixture.CommitSize * 2).ToListAsync();
		var lastPosition = logEvents.First().OriginalPosition!.Value;
		var firstPosition = logEvents.Last().OriginalPosition!.Value;

		var processor = Fixture.NodeServices.GetRequiredService<DefaultIndexProcessor>();
		switch (mode) {
			case CommitMode.CommitAndClear:
				processor.Commit(true);
				break;
			case CommitMode.CommitAndKeep:
				processor.Commit(false);
				break;
		}

		while (processor.LastIndexedPosition < lastPosition) {
			await Task.Delay(500);
		}

		var records = logEvents.Select(x => x.Event).ToArray();
		return (records, Position.FromInt64(firstPosition.CommitPosition, firstPosition.PreparePosition));
	}

	private async Task ValidateRead(string indexName, ResolvedEvent[] expectedEvents, bool forwards) {
		var results = await Fixture.ReadUntil(indexName, expectedEvents.Length, forwards);
		var expected = forwards ? expectedEvents : expectedEvents.Reverse().ToArray();

		AssertResolvedEventsMatch(results, expected, forwards);
	}

	private class CategoryFilter(string category) : IEventFilter {
		public bool IsEventAllowed(EventRecord eventRecord) => eventRecord.EventStreamId.StartsWith($"{category}-");
	}

	private class EventTypeFilter(string eventType) : IEventFilter {
		public bool IsEventAllowed(EventRecord eventRecord) => eventRecord.EventType == eventType;
	}
}
