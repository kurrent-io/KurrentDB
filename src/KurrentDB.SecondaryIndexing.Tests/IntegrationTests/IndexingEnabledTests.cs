// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
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
		TotalMessagesCount: 100
	);

	private readonly List<TestMessageBatch> _appendedBatches = [];

	private int TotalMessagesCount =>
		_appendedBatches.Count > 0 ? _appendedBatches.Sum(b => b.Messages.Length) : 0;

	private string[] Categories =>
		_appendedBatches.Select(b => b.CategoryName).Distinct().ToArray();

	private string[] EventTypes =>
		_appendedBatches.SelectMany(b => b.Messages.Select(m => m.EventType)).Distinct().ToArray();

	public override async Task InitializeAsync() {
		await foreach (var batch in _messageGenerator.GenerateBatches(_config)) {
			var messages = batch.Messages.Select(m => m.ToEventData()).ToArray();
			await fixture.AppendToStream(batch.StreamName, messages);
			_appendedBatches.Add(batch);
		}
	}

	[Fact]
	public Task ReadsAllEventsFromDefaultIndex() =>
		ValidateRead(DefaultIndex.IndexName, TotalMessagesCount);

	[Fact]
	public async Task ReadsAllEventsFromCategoryIndex() {
		(string CategoryName, int MessagesCount)[] indexes = Categories
			.Select(category =>
			(
				$"{CategoryIndex.IndexPrefix}{category}",
				_appendedBatches.Where(c => c.CategoryName == category).Sum(c => c.Messages.Length)
			)).ToArray();

		foreach (var index in indexes) {
			await ValidateRead(index.CategoryName, index.MessagesCount);
		}
	}

	[Fact]
	public async Task ReadsAllEventsFromEventTypeIndex() {
		(string EventType, int MessagesCount)[] indexes = EventTypes
			.Select(eventType =>
			(
				$"{EventTypeIndex.IndexPrefix}{eventType}",
				_appendedBatches.Sum(c => c.Messages.Where(e => e.EventType == eventType).Count())
			)).ToArray();

		foreach (var index in indexes) {
			await ValidateRead(index.EventType, index.MessagesCount);
		}
	}

	[Fact(Skip = "TODO")]
	public Task SubscriptionReturnsAllEventsFromDefaultIndex() =>
		ValidateSubscription(DefaultIndex.IndexName, TotalMessagesCount);

	[Fact(Skip = "TODO")]
	public async Task SubscriptionReturnsAllEventsFromCategoryIndex() {
		(string CategoryName, int MessagesCount)[] indexes = Categories
			.Select(category =>
			(
				$"{CategoryIndex.IndexPrefix}{category}",
				_appendedBatches.Where(c => c.CategoryName == category).Sum(c => c.Messages.Length)
			)).ToArray();

		foreach (var index in indexes) {
			await ValidateSubscription(index.CategoryName, index.MessagesCount);
		}
	}

	[Fact(Skip = "TODO")]
	public async Task SubscriptionReturnsAllEventsFromEventTypeIndex() {
		(string EventType, int MessagesCount)[] indexes = EventTypes
			.Select(eventType =>
			(
				$"{EventTypeIndex.IndexPrefix}{eventType}",
				_appendedBatches.Sum(c => c.Messages.Where(e => e.EventType == eventType).Count())
			)).ToArray();

		foreach (var index in indexes) {
			await ValidateSubscription(index.EventType, index.MessagesCount);
		}
	}

	private async Task ValidateRead(string index, int maxCount) {
		var results = await fixture.ReadUntil(index, maxCount);

		Assert.NotEmpty(results);
		Assert.Equal(maxCount, results.Count);
	}

	private async Task ValidateSubscription(string index, int maxCount) {
		var results = await fixture.SubscribeUntil(index, maxCount);

		Assert.NotEmpty(results);
		Assert.Equal(maxCount, results.Count);
	}
}
