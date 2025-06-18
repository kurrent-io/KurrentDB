// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.LoadTesting.Environments;
using KurrentDB.SecondaryIndexing.Tests.Generators;
using KurrentDB.SecondaryIndexing.Tests.Observability;

namespace KurrentDB.SecondaryIndexing.LoadTesting;

public class LoadTest(IMessageGenerator generator, ILoadTestEnvironment environment, IMessagesBatchObserver observer) {
	public async Task Run(LoadTestConfig config) {
		await environment.InitializeAsync();
		var testPartitions = config.GeneratePartitions();

		await Task.WhenAll(testPartitions.Select(ProcessPartition));

		await environment.AssertThat.IndexesMatch(observer.Summary);
	}

	private async Task ProcessPartition(LoadTestPartitionConfig loadTestPartitionConfig) {
		await foreach (var messageBatch in generator.GenerateBatches(loadTestPartitionConfig)) {
			await environment.MessageBatchAppender.Append(messageBatch);
			observer.On(messageBatch);
		}
	}
}
