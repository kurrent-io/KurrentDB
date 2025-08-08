// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Tests.Fixtures;
using KurrentDB.SecondaryIndexing.Tests.Generators;
using Xunit.Abstractions;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

[Trait("Category", "Integration")]
public class IndexingTests(IndexingFixture fixture, ITestOutputHelper output)
	: SecondaryIndexingTest<IndexingFixture>(fixture, output) {
	[Fact]
	public Task ReadsAllEventsFromDefaultIndex() =>
		ValidateRead(SystemStreams.DefaultSecondaryIndex, Fixture.AppendedBatches.ToDefaultIndexResolvedEvents());

	[Fact]
	public async Task ReadsAllEventsFromCategoryIndex() {
		foreach (var category in Fixture.Categories) {
			var expectedEvents = Fixture.AppendedBatches.ToCategoryIndexResolvedEvents(category);
			await ValidateRead(CategoryIndex.Name(category), expectedEvents);
		}
	}

	[Fact]
	public async Task ReadsAllEventsFromEventTypeIndex() {
		foreach (var eventType in Fixture.EventTypes) {
			var expectedEvents = Fixture.AppendedBatches.ToEventTypeIndexResolvedEvents(eventType);
			await ValidateRead(EventTypeIndex.Name(eventType), expectedEvents);
		}
	}

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

	async Task ValidateRead(string indexStreamName, ResolvedEvent[] expectedEvents) {
		var results = await Fixture.ReadUntil(indexStreamName, expectedEvents.Length);

		AssertResolvedEventsMatch(results, expectedEvents);
	}

	async Task ValidateSubscription(string indexStreamName, ResolvedEvent[] expectedEvents) {
		var results = await Fixture.SubscribeUntil(indexStreamName, expectedEvents.Length);

		AssertResolvedEventsMatch(results, expectedEvents);
	}

	static void AssertResolvedEventsMatch(List<ResolvedEvent> results, ResolvedEvent[] expectedRecords) {
		Assert.NotEmpty(results);
		Assert.Equal(expectedRecords.Length, results.Count);

		Assert.All(results,
			(item, index) => {
				Assert.NotEqual(0L, item.Event.LogPosition);
				Assert.NotEqual(0L, item.Event.TransactionPosition);

				if (index == 0)
					return;

				var previousItem = results[index - 1];

				Assert.True(item.Event.LogPosition > previousItem.Event.LogPosition);
			});

		Assert.All(results, item => Assert.NotEqual(default, item.Event.TimeStamp));

		for (var sequence = 0; sequence < results.Count; sequence++) {
			var actual = results[sequence];
			var expected = expectedRecords[sequence];

			Assert.Equal(expected.Event.EventId, actual.Event.EventId);
			Assert.Equal(expected.Event.EventType, actual.Event.EventType);
			Assert.Equal(expected.Event.Data, actual.Event.Data);
			Assert.Equal(expected.Event.EventNumber, actual.Event.EventNumber);

			Assert.NotEqual(default, actual.Event.Flags);
			Assert.Equal(expected.Event.Metadata, actual.Event.Metadata);
			Assert.Equal(actual.Event.TransactionOffset, actual.Event.TransactionOffset);
			Assert.Equal(ReadEventResult.Success, actual.ResolveResult);
		}
	}
}

[UsedImplicitly]
public class IndexingFixture : SecondaryIndexingEnabledFixture {
	readonly LoadTestPartitionConfig _config = new(
		PartitionId: 1,
		StartCategoryIndex: 0,
		CategoriesCount: 5,
		MaxStreamsPerCategory: 100,
		MessageTypesCount: 10,
		MessageSize: 10,
		MaxBatchSize: 2,
		TotalMessagesCount: 10
	);

	readonly MessageGenerator _messageGenerator = new();

	public IndexingFixture() {
		OnSetup = async () => {
			await foreach (var batch in _messageGenerator.GenerateBatches(_config)) {
				var messages = batch.Messages.Select(m => m.ToEventData()).ToArray();
				await AppendToStream(batch.StreamName, messages);
				AppendedBatches.Add(batch);
			}
		};
	}

	public readonly List<TestMessageBatch> AppendedBatches = [];

	public List<TestMessageBatch> ExpectedBatches => AppendedBatches.ToList();

	public string[] Categories => AppendedBatches.Select(b => b.CategoryName).Distinct().ToArray();

	public string[] EventTypes => AppendedBatches.SelectMany(b => b.Messages.Select(m => m.EventType)).Distinct().ToArray();
}
