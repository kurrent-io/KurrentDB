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
public class DeletingStreamsTests(DeletingStreamsFixture fixture, ITestOutputHelper output) : SecondaryIndexingTest<DeletingStreamsFixture>(fixture, output) {
	[Fact(Skip = "For now")]
	public Task ReadsAllEventsFromDefaultIndex() =>
		ValidateRead(SystemStreams.DefaultSecondaryIndex, Fixture.ExpectedBatches.ToDefaultIndexResolvedEvents());

	[Fact(Skip = "For now")]
	public async Task ReadsAllEventsFromCategoryIndex() {
		foreach (var category in Fixture.Categories) {
			var expectedEvents = Fixture.ExpectedBatches.ToCategoryIndexResolvedEvents(category);
			await ValidateRead(CategoryIndex.Name(category), expectedEvents);
		}
	}

	[Fact(Skip = "For now")]
	public async Task ReadsAllEventsFromEventTypeIndex() {
		foreach (var eventType in Fixture.EventTypes) {
			var expectedEvents = Fixture.ExpectedBatches.ToEventTypeIndexResolvedEvents(eventType);
			await ValidateRead(EventTypeIndex.Name(eventType), expectedEvents);
		}
	}

	[Fact(Skip = "For now")]
	public Task SubscriptionReturnsAllEventsFromDefaultIndex() =>
		ValidateSubscription(SystemStreams.DefaultSecondaryIndex, Fixture.ExpectedBatches.ToDefaultIndexResolvedEvents());

	[Fact(Skip = "For now")]
	public async Task SubscriptionReturnsAllEventsFromCategoryIndex() {
		foreach (var category in Fixture.Categories) {
			var expectedEvents = Fixture.ExpectedBatches.ToCategoryIndexResolvedEvents(category);
			await ValidateSubscription(CategoryIndex.Name(category), expectedEvents);
		}
	}

	[Fact(Skip = "For now")]
	public async Task SubscriptionReturnsAllEventsFromEventTypeIndex() {
		foreach (var eventType in Fixture.EventTypes) {
			var expectedEvents = Fixture.ExpectedBatches.ToEventTypeIndexResolvedEvents(eventType);
			await ValidateSubscription(EventTypeIndex.Name(eventType), expectedEvents);
		}
	}

	private async Task ValidateRead(string indexName, ResolvedEvent[] expectedEvents) {
		var results = await Fixture.ReadUntil(indexName, expectedEvents.Length, true);

		AssertResolvedEventsMatch(results, expectedEvents);
	}

	private async Task ValidateSubscription(string indexName, ResolvedEvent[] expectedEvents) {
		var results = await Fixture.SubscribeUntil(indexName, expectedEvents.Length);

		AssertResolvedEventsMatch(results, expectedEvents);
	}

	private static void AssertResolvedEventsMatch(List<ResolvedEvent> results, ResolvedEvent[] expectedRecords) {
		Assert.NotEmpty(results);
		Assert.Equal(expectedRecords.Length, results.Count);

		Assert.All(results, (item, index) => {
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
public class DeletingStreamsFixture : SecondaryIndexingEnabledFixture {
	private readonly LoadTestPartitionConfig _config = new(
		PartitionId: 1,
		StartCategoryIndex: 0,
		CategoriesCount: 5,
		MaxStreamsPerCategory: 100,
		MessageTypesCount: 10,
		MessageSize: 10,
		MaxBatchSize: 2,
		TotalMessagesCount: 10
	);

	private readonly MessageGenerator _messageGenerator = new();

	public DeletingStreamsFixture() {
		OnSetup = async () => {
			await foreach (var batch in _messageGenerator.GenerateBatches(_config)) {
				var messages = batch.Messages.Select(m => m.ToEventData()).ToArray();
				await AppendToStream(batch.StreamName, messages);
				_appendedBatches.Add(batch);
			}

			DeletedStreamName = GetRandomStreamNameFromAppended();
			await DeleteStream(DeletedStreamName);
		};
	}

	private static string DeletedStreamName = null!;

	private readonly List<TestMessageBatch> _appendedBatches = [];

	public List<TestMessageBatch> ExpectedBatches => _appendedBatches.Where(b => b.StreamName != DeletedStreamName).ToList();

	private string GetRandomStreamNameFromAppended() => _appendedBatches.Select(b => b.StreamName).Distinct().ToList().RandomElement();

	public string[] Categories => _appendedBatches.Select(b => b.CategoryName).Distinct().ToArray();

	public string[] EventTypes => _appendedBatches.SelectMany(b => b.Messages.Select(m => m.EventType)).Distinct().ToArray();
}
