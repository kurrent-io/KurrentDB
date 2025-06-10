// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.LoadTesting.Generators;
using KurrentDB.SecondaryIndexing.LoadTesting.Observability;

namespace KurrentDB.SecondaryIndexing.LoadTesting;

public class LoadTest(IMessageGenerator generator, IMessagesAppender appender, IMessagesBatchObserver observer) {
	public async Task Run(LoadTestConfig config) {
		var testShards = LoadTestShardConfig.From(config);

		await Task.WhenAll(testShards.Select(ProcessShard));

		Debug.Assert(observer.TotalCount == config.TotalMessagesCount);
		Debug.Assert(observer.Categories.Values.Sum() == config.TotalMessagesCount);
		Debug.Assert(observer.EventTypes.Values.Sum() == config.TotalMessagesCount);
	}

	private async Task ProcessShard(LoadTestShardConfig loadTestShardConfig) {
		await foreach (var messageBatch in generator.GenerateBatches(loadTestShardConfig)) {
			await appender.Append(messageBatch);
			observer.On(messageBatch);
		}
	}
}
