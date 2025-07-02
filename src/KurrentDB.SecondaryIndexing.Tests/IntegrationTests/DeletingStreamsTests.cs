// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Tests.Fixtures;
using KurrentDB.SecondaryIndexing.Tests.Generators;
using Xunit.Abstractions;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

[Trait("Category", "Integration")]
[Collection("SecondaryIndexingPluginEnabled")]
public class DeletingStreamsTests(
	SecondaryIndexingEnabledFixture fixture,
	ITestOutputHelper output
) : SecondaryIndexingTestBase(fixture, output) {
	private readonly MessageGenerator _messageGenerator = new();

	private readonly LoadTestPartitionConfig _config = new(
		PartitionId: 1,
		StartCategoryIndex: 0,
		CategoriesCount: 5,
		MaxStreamsPerCategory: 100,
		MessageTypesCount: 10,
		MessageSize: 10,
		MaxBatchSize: 2,
		TotalMessagesCount: 1000
	);

	private static string DeletedStreamName = null!;

	public override async Task BeforeAll() {
		await foreach (var batch in _messageGenerator.GenerateBatches(_config)) {
			var messages = batch.Messages.Select(m => m.ToEventData()).ToArray();
			await fixture.AppendToStream(batch.StreamName, messages);
			AppendedBatches.Add(batch);
		}

		DeletedStreamName = GetRandomStreamNameFromAppended();
		await fixture.DeleteStream(DeletedStreamName);
	}

	[Fact]
	public Task ReadsAllEventsFromDefaultIndex() =>
		ValidateRead(DefaultIndex.Name, ExpectedBatches.ToDefaultIndexResolvedEvents());

	[Fact]
	public async Task ReadsAllEventsFromCategoryIndex() {
		foreach (var category in Categories) {
			var expectedEvents = ExpectedBatches.ToCategoryIndexResolvedEvents(category);
			await ValidateRead(CategoryIndex.Name(category), expectedEvents);
		}
	}

	[Fact]
	public async Task ReadsAllEventsFromEventTypeIndex() {
		foreach (var eventType in EventTypes) {
			var expectedEvents = ExpectedBatches.ToEventTypeIndexResolvedEvents(eventType);
			await ValidateRead(EventTypeIndex.Name(eventType), expectedEvents);
		}
	}

	[Fact]
	public Task SubscriptionReturnsAllEventsFromDefaultIndex() =>
		ValidateSubscription(DefaultIndex.Name, ExpectedBatches.ToDefaultIndexResolvedEvents());

	[Fact]
	public async Task SubscriptionReturnsAllEventsFromCategoryIndex() {
		foreach (var category in Categories) {
			var expectedEvents = ExpectedBatches.ToCategoryIndexResolvedEvents(category);
			await ValidateSubscription(CategoryIndex.Name(category), expectedEvents);
		}
	}

	[Fact]
	public async Task SubscriptionReturnsAllEventsFromEventTypeIndex() {
		foreach (var eventType in EventTypes) {
			var expectedEvents = ExpectedBatches.ToEventTypeIndexResolvedEvents(eventType);
			await ValidateSubscription(EventTypeIndex.Name(eventType), expectedEvents);
		}
	}

	private async Task ValidateRead(string indexStreamName, ResolvedEvent[] expectedEvents) {
		var results = await fixture.ReadUntil(indexStreamName, expectedEvents.Length);

		AssertResolvedEventsMatch(indexStreamName, results, expectedEvents);
	}

	private async Task ValidateSubscription(string indexStreamName, ResolvedEvent[] expectedEvents) {
		var results = await fixture.SubscribeUntil(indexStreamName, expectedEvents.Length);

		AssertResolvedEventsMatch(indexStreamName, results, expectedEvents);
	}

	private static void AssertResolvedEventsMatch(
		string indexStreamName,
		List<ResolvedEvent> results,
		ResolvedEvent[] expectedRecords
	) {
		Assert.NotEmpty(results);
		Assert.Equal(expectedRecords.Length, results.Count);

		Assert.All(results, item => {
			Assert.NotNull(item.Link);
			Assert.Equal("$>", item.Link.EventType);
			Assert.NotEqual("$>", item.Event.EventType);
		});

		Assert.All(results, (item, index) => {
			Assert.Equal(index, item.Link.EventNumber);
			Assert.Equal(index - 1, item.Link.ExpectedVersion);
		});

		Assert.All(results,
			(item, index) => {
				Assert.Equal(item.Event.LogPosition, item.Link.LogPosition);
				Assert.Equal(item.Event.TransactionOffset, item.Link.TransactionOffset);
				Assert.Equal(item.Event.TransactionPosition, item.Link.TransactionPosition);

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
			Assert.Equal(actual.Link.EventId, actual.Event.EventId);

			Assert.Equal(indexStreamName, actual.Link.EventStreamId);
			Assert.NotEqual(actual.Link.EventStreamId, actual.Event.EventStreamId);
			Assert.Equal(expected.Link.EventStreamId, actual.Link.EventStreamId);

			Assert.Equal(expected.Event.EventType, actual.Event.EventType);

			Assert.Equal(expected.Event.Data, actual.Event.Data);
			//TODO: Check as for some reason that fails sometimes for subscription
			Assert.Equal(expected.Link.Data, actual.Link.Data);

			Assert.Equal(expected.Event.EventNumber, actual.Event.EventNumber);
			Assert.Equal(sequence, actual.Link.EventNumber);

			Assert.Equal(expected.Link.IsJson, actual.Link.IsJson);
			Assert.NotEqual(default, actual.Event.Flags);
			Assert.Equal(expected.Event.Metadata, actual.Event.Metadata);
			Assert.Equal(actual.Event.TransactionOffset, actual.Event.TransactionOffset);
			Assert.Equal(ReadEventResult.Success, actual.ResolveResult);
		}
	}

	private static readonly List<TestMessageBatch> AppendedBatches = [];
	private static List<TestMessageBatch> ExpectedBatches =>
		AppendedBatches.Where(b => b.StreamName != DeletedStreamName).ToList();

	private static string GetRandomStreamNameFromAppended() =>
		AppendedBatches.Select(b => b.StreamName).Distinct().ToList().RandomElement();

	private static string[] Categories =>
		AppendedBatches.Select(b => b.CategoryName).Distinct().ToArray();

	private static string[] EventTypes =>
		AppendedBatches.SelectMany(b => b.Messages.Select(m => m.EventType)).Distinct().ToArray();
}
