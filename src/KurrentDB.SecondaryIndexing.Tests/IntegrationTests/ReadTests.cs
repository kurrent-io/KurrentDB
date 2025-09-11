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

	[Fact]
	public async Task ReadFromBoth() {
		var logEvents = await Fixture.Publisher.ReadBackwards(Position.End, new EventFilter.DefaultAllFilterStrategy.NonSystemStreamStrategy(), Fixture.CommitSize * 2).ToListAsync();
		var lastPosition = logEvents.First().OriginalPosition!.Value;
		var firstPosition = logEvents.Last().OriginalPosition!.Value;

		var processor = Fixture.NodeServices.GetRequiredService<DefaultIndexProcessor>();

		while (processor.LastIndexedPosition < lastPosition) {
			await Task.Delay(500);
		}

		var resultBwd = await Fixture.Publisher.ReadIndex("$idx-all", Position.End, logEvents.Count, forwards: false).ToListAsync();
		var actualBwd = resultBwd.Select(x => x.Event).ToArray();

		var expectedBwd = logEvents.Select(x => x.Event).ToArray();
		Assert.Equal(expectedBwd, actualBwd);

		var resultFwd = await Fixture.Publisher.ReadIndex("$idx-all", Position.FromInt64(firstPosition.CommitPosition, firstPosition.PreparePosition), long.MaxValue).ToListAsync();
		var actualFwd = resultFwd.Select(x => x.Event).ToArray();

		logEvents.Reverse();
		var expectedFwd = logEvents.Select(x => x.Event).ToArray();
		Assert.Equal(expectedFwd, actualFwd);
	}

	private async Task ValidateRead(string indexName, ResolvedEvent[] expectedEvents, bool forwards) {
		var results = await Fixture.ReadUntil(indexName, expectedEvents.Length, forwards);
		var expected = forwards ? expectedEvents : expectedEvents.Reverse().ToArray();

		AssertResolvedEventsMatch(results, expected, forwards);
	}
}
