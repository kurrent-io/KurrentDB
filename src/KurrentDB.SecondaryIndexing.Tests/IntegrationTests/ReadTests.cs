// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Tests.Generators;
using KurrentDB.Surge.Testing;
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

	private async Task ValidateRead(string indexName, ResolvedEvent[] expectedEvents, bool forwards) {
		var results = await Fixture.ReadUntil(indexName, expectedEvents.Length, forwards);
		var expected = forwards ? expectedEvents : expectedEvents.Reverse().ToArray();

		AssertResolvedEventsMatch(results, expected, forwards);
	}
}
