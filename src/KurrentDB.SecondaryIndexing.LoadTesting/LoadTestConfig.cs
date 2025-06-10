// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.SecondaryIndexing.LoadTesting;

public enum ExecutionMode {
	InMemory,
	Container
}

public class LoadTestConfig {
	public int ShardsCount { get; set; } = 1;
	public int CategoriesCount { get; set; } = 10;
	public int MaxStreamsPerCategory { get; set; } = 100;
	public int MessageTypesPerCategoryCount { get; set; } = 10;
	public int MessageSize { get; set; } = 400;
	public int MaxBatchSize { get; set; } = 2;
	public int TotalMessagesCount { get; set; } = 1010;
	public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.InMemory;
	public required string KurrentDBConnectionString { get; set; } = "Dummy";
	public required string DuckDbConnectionString { get; set; }= "Dummy";
}

public record LoadTestShardConfig(
	int ShardId,
	int StartCategoryIndex,
	int CategoriesCount,
	int MaxStreamsPerCategory,
	int MessageTypesCount,
	int MessageSize,
	int MaxBatchSize,
	int TotalMessagesCount
) {
	public static LoadTestShardConfig[] From(LoadTestConfig loadTestConfig) {
		var categoriesPerShard = loadTestConfig.CategoriesCount / loadTestConfig.ShardsCount;
		var messagesPerShard = loadTestConfig.TotalMessagesCount / loadTestConfig.ShardsCount;

		var shards = new LoadTestShardConfig[loadTestConfig.ShardsCount];

		for (int i = 0; i < loadTestConfig.ShardsCount; i++) {
			var isLastShard = i == loadTestConfig.ShardsCount - 1;

			shards[i] = new LoadTestShardConfig(
				ShardId: i,
				StartCategoryIndex: i * categoriesPerShard,
				CategoriesCount: isLastShard
					? loadTestConfig.CategoriesCount - i * categoriesPerShard
					: categoriesPerShard,
				MaxStreamsPerCategory: loadTestConfig.MaxStreamsPerCategory,
				MessageTypesCount: loadTestConfig.MessageTypesPerCategoryCount,
				MessageSize: loadTestConfig.MessageSize,
				MaxBatchSize: loadTestConfig.MaxBatchSize,
				TotalMessagesCount: isLastShard
					? loadTestConfig.TotalMessagesCount - i * messagesPerShard
					: messagesPerShard
			);
		}

		return shards;
	}
}
