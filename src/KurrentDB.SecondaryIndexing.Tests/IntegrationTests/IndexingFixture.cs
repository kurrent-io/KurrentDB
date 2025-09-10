// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.Tests.Fixtures;
using KurrentDB.SecondaryIndexing.Tests.Generators;
using Microsoft.Extensions.Logging;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests;

[UsedImplicitly]
public class IndexingFixture : SecondaryIndexingEnabledFixture {
	private readonly LoadTestPartitionConfig _config = new(
		PartitionId: 1,
		StartCategoryIndex: 0,
		CategoriesCount: 5,
		MaxStreamsPerCategory: 100,
		MessageTypesCount: 10,
		MessageSize: 10,
		MaxBatchSize: 2,
		TotalMessagesCount: 1850
	);

	private readonly MessageGenerator _messageGenerator = new();

	public IndexingFixture() {
		OnSetup = async () => {
			_total = 0;
			await foreach (var batch in _messageGenerator.GenerateBatches(_config)) {
				var messages = batch.Messages.Select(m => m.ToEventData()).ToArray();
				await AppendToStream(batch.StreamName, messages);
				_total += messages.Length;
				AppendedBatches.Add(batch);
			}
		};
	}

	public readonly List<TestMessageBatch> AppendedBatches = [];
	private int _total;

	public void LogDatasetInfo() {
		Logger.LogInformation("Using {Batches} batches with total {Count} records", AppendedBatches.Count, _total);
	}

	public string[] Categories => AppendedBatches.Select(b => b.CategoryName).Distinct().ToArray();

	public string[] EventTypes => AppendedBatches.SelectMany(b => b.Messages.Select(m => m.EventType)).Distinct().ToArray();
}
