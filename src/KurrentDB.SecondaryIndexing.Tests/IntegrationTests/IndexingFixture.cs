// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Services.Transport.Common;
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
			TotalMessagesCount = 0;
			await foreach (var batch in _messageGenerator.GenerateBatches(_config)) {
				var messages = batch.Messages.Select(m => m.ToEventData()).ToArray();
				var result = await AppendToStream(batch.StreamName, messages);
				TotalMessagesCount += messages.Length;
				AppendedBatches.Add((batch, result.Position));
			}
		};
	}

	public readonly List<(TestMessageBatch Batch, Position Position)> AppendedBatches = [];

	public void LogDatasetInfo() {
		Logger.LogInformation("Using {Batches} batches with total {Count} records", AppendedBatches.Count, TotalMessagesCount);
	}

	public string[] Categories => AppendedBatches.Select(b => b.Batch.CategoryName).Distinct().ToArray();

	public string[] EventTypes => AppendedBatches.SelectMany(b => b.Batch.Messages.Select(m => m.EventType)).Distinct().ToArray();

	public int TotalMessagesCount { get; private set; }
}
