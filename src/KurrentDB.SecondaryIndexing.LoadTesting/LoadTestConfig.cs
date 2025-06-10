// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.LoadTesting.Environments;

namespace KurrentDB.SecondaryIndexing.LoadTesting;


public class LoadTestConfig {
	public int PartitionsCount { get; set; } = 1;
	public int CategoriesCount { get; set; } = 10;
	public int MaxStreamsPerCategory { get; set; } = 100;
	public int MessageTypesPerCategoryCount { get; set; } = 10;
	public int MessageSize { get; set; } = 400;
	public int MaxBatchSize { get; set; } = 2;
	public int TotalMessagesCount { get; set; } = 1010;
	public LoadTestEnvironmentType EnvironmentType { get; set; } = LoadTestEnvironmentType.TestServer;
	public required string KurrentDBConnectionString { get; set; } = "Dummy";
	public required string DuckDbConnectionString { get; set; }= "Dummy";
}

public record LoadTestPartitionConfig(
	int PartitionId,
	int StartCategoryIndex,
	int CategoriesCount,
	int MaxStreamsPerCategory,
	int MessageTypesCount,
	int MessageSize,
	int MaxBatchSize,
	int TotalMessagesCount
) {
	public static LoadTestPartitionConfig[] From(LoadTestConfig loadTestConfig) {
		var categoriesPerPartition = loadTestConfig.CategoriesCount / loadTestConfig.PartitionsCount;
		var messagesPerPartition = loadTestConfig.TotalMessagesCount / loadTestConfig.PartitionsCount;

		var partitions = new LoadTestPartitionConfig[loadTestConfig.PartitionsCount];

		for (int i = 0; i < loadTestConfig.PartitionsCount; i++) {
			var isLastPartition = i == loadTestConfig.PartitionsCount - 1;

			partitions[i] = new LoadTestPartitionConfig(
				PartitionId: i,
				StartCategoryIndex: i * categoriesPerPartition,
				CategoriesCount: isLastPartition
					? loadTestConfig.CategoriesCount - i * categoriesPerPartition
					: categoriesPerPartition,
				MaxStreamsPerCategory: loadTestConfig.MaxStreamsPerCategory,
				MessageTypesCount: loadTestConfig.MessageTypesPerCategoryCount,
				MessageSize: loadTestConfig.MessageSize,
				MaxBatchSize: loadTestConfig.MaxBatchSize,
				TotalMessagesCount: isLastPartition
					? loadTestConfig.TotalMessagesCount - i * messagesPerPartition
					: messagesPerPartition
			);
		}

		return partitions;
	}
}
