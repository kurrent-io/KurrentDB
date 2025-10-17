// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Tests.Generators;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

public partial class IndexingTests {
	[Fact]
	public Task SubscriptionReturnsAllEventsFromDefaultIndex() =>
		ValidateSubscription(SystemStreams.DefaultSecondaryIndex, Fixture.AppendedBatches.ToDefaultIndexResolvedEvents());

	[Fact]
	public async Task SubscriptionReturnsAllEventsFromCategoryIndex() {
		foreach (var category in Fixture.Categories) {
			var expectedEvents = Fixture.AppendedBatches.ToCategoryIndexResolvedEvents(category);
			await ValidateSubscription(CategoryIndex.Name(category), expectedEvents);
		}
	}

	[Fact]
	public async Task SubscriptionReturnsAllEventsFromEventTypeIndex() {
		foreach (var eventType in Fixture.EventTypes) {
			var expectedEvents = Fixture.AppendedBatches.ToEventTypeIndexResolvedEvents(eventType);
			await ValidateSubscription(EventTypeIndex.Name(eventType), expectedEvents);
		}
	}

	[Fact]
	public async Task SubscribeToUnknownIndexFails() {
		const string indexName = "$idx-dummy";
		var exception = await Assert.ThrowsAsync<ReadResponseException.IndexNotFound>(() => Fixture.SubscribeUntil(indexName, 100));
		Assert.Equal(indexName, exception.IndexName);
	}

	private async Task ValidateSubscription(string indexName, ResolvedEvent[] expectedEvents) {
		var results = await Fixture.SubscribeUntil(indexName, expectedEvents.Length);

		AssertResolvedEventsMatch(results, expectedEvents, true);
	}
}
