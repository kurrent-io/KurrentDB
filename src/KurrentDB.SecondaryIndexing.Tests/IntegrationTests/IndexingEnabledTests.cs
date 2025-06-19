// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Amazon.S3;
using KurrentDB.Core.Data;
using KurrentDB.Core.Tests;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Tests.Fixtures;
using KurrentDB.SecondaryIndexing.Tests.Generators;
using Xunit.Abstractions;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

[Trait("Category", "Integration")]
[Collection("SecondaryIndexingPluginEnabled")]
public class IndexingEnabledTests(
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

	public override async Task BeforeAll() {
		await foreach (var batch in _messageGenerator.GenerateBatches(_config)) {
			var messages = batch.Messages.Select(m => m.ToEventData()).ToArray();
			await fixture.AppendToStream(batch.StreamName, messages);
			_appendedBatches.Add(batch);
		}
	}

	[Fact]
	public Task ReadsAllEventsFromDefaultIndex() =>
		ValidateRead(DefaultIndex.IndexName, _appendedBatches.ToDefaultIndexResolvedEvents());

	[Fact]
	public async Task ReadsAllEventsFromCategoryIndex() {
		foreach (var category in Categories) {
			var expectedEvents = _appendedBatches.ToCategoryIndexResolvedEvents(category);
			await ValidateRead($"{CategoryIndex.IndexPrefix}{category}", expectedEvents);
		}
	}

	[Fact]
	public async Task ReadsAllEventsFromEventTypeIndex() {
		foreach (var eventType in EventTypes) {
			var expectedEvents = _appendedBatches.ToEventTypeIndexResolvedEvents(eventType);
			await ValidateRead($"{EventTypeIndex.IndexPrefix}{eventType}", expectedEvents);
		}
	}

	[Fact(Skip = "TODO: Fix live subscriptions")]
	public Task SubscriptionReturnsAllEventsFromDefaultIndex() =>
		ValidateSubscription(DefaultIndex.IndexName, _appendedBatches.ToDefaultIndexResolvedEvents());

	[Fact]
	public async Task SubscriptionReturnsAllEventsFromCategoryIndex() {
		foreach (var category in Categories) {
			var expectedEvents = _appendedBatches.ToCategoryIndexResolvedEvents(category);
			await ValidateSubscription($"{CategoryIndex.IndexPrefix}{category}", expectedEvents);
		}
	}

	[Fact]
	public async Task SubscriptionReturnsAllEventsFromEventTypeIndex() {
		foreach (var eventType in EventTypes) {
			var expectedEvents = _appendedBatches.ToEventTypeIndexResolvedEvents(eventType);
			await ValidateSubscription($"{EventTypeIndex.IndexPrefix}{eventType}", expectedEvents);
		}
	}

	private async Task ValidateRead(string indexStreamName, ResolvedEvent[] expectedEvents) {
		var results = await fixture.ReadUntil(indexStreamName, expectedEvents.Length);

		AssertResolvedEventsMatch(indexStreamName, results, expectedEvents);
	}

	private async Task ValidateSubscription(string indexStreamName, ResolvedEvent[] expectedEvents) {
		await Task.Delay(15000); //TODO: Remove when live mode works well
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
			Assert.Equal(index - 1, item.Link.ExpectedVersion - 1);
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

	private readonly List<TestMessageBatch> _appendedBatches = [];

	private string[] Categories =>
		_appendedBatches.Select(b => b.CategoryName).Distinct().ToArray();

	private string[] EventTypes =>
		_appendedBatches.SelectMany(b => b.Messages.Select(m => m.EventType)).Distinct().ToArray();
}
