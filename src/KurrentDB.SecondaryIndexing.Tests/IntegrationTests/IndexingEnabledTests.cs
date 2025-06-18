// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

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

	private readonly List<TestMessageBatch> _appendedBatches = [];

	private int TotalMessagesCount =>
		_appendedBatches.Count > 0 ? _appendedBatches.Sum(b => b.Messages.Length) : 0;

	private string[] Categories =>
		_appendedBatches.Select(b => b.CategoryName).Distinct().ToArray();

	private string[] EventTypes =>
		_appendedBatches.SelectMany(b => b.Messages.Select(m => m.EventType)).Distinct().ToArray();

	[Fact]
	public async Task Appended_events_are_indexed() {
		await foreach (var batch in _messageGenerator.GenerateBatches(_config)) {
			var messages = batch.Messages.Select(m => m.ToEventData()).ToArray();
			await fixture.AppendToStream(batch.StreamName, messages);
			_appendedBatches.Add(batch);
		}

		(string Name, int MessagesCount)[] indexes = [
			(DefaultIndex.IndexName, TotalMessagesCount),
			// ..Categories.Select(category =>
			// 	(
			// 		$"{CategoryIndex.IndexPrefix}{category}",
			// 		_appendedBatches.Where(c => c.CategoryName == category).Sum(c => c.Messages.Length)
			// 	)
			// ),
			// ..EventTypes.Select(eventType =>
			// 	(
			// 		$"{EventTypeIndex.IndexPrefix}{eventType}",
			// 		_appendedBatches.Sum(c => c.Messages.Where(e => e.EventType == eventType).Count())
			// 	)
			// ),
		];
		foreach (var index in indexes) {
			//await ValidateRead(index.Name, index.MessagesCount);
			await ValidateSubscription(index.Name, index.MessagesCount);
		}
	}

	async Task ValidateRead(string index, int maxCount) {
		var results = await fixture.ReadUntil(index, maxCount);

		Assert.NotEmpty(results);
		// var results = readResult.Where(e => e.Event.EventStreamId == streamName).ToList();
		Assert.Equal(maxCount, results.Count);
		//Assert.All(results, e => Assert.Contains(Encoding.UTF8.GetString(e.Event.Data.Span), _expectedEventData));
	}

	private async Task ValidateSubscription(string index, int maxCount) {
		var results = await fixture.SubscribeUntil(index, maxCount);

		Assert.NotEmpty(results);
		// var results = readResult.Where(e => e.Event.EventStreamId == streamName).ToList();
		Assert.Equal(maxCount, results.Count);
		// Assert.All(results, e => Assert.Contains(Encoding.UTF8.GetString(e.Event.Data.Span), _expectedEventData));
	}
}
