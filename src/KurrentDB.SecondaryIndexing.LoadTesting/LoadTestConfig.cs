// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.LoadTesting.Environments;
using KurrentDB.SecondaryIndexing.LoadTesting.Environments.DuckDB;
using KurrentDB.SecondaryIndexing.LoadTesting.Environments.Indexes;
using KurrentDB.SecondaryIndexing.Tests.Generators;

namespace KurrentDB.SecondaryIndexing.LoadTesting;

public class LoadTestConfig {
	public int PartitionsCount { get; set; } = 1;
	public int CategoriesCount { get; set; } = 10;
	public int MaxStreamsPerCategory { get; set; } = 100000;
	public int MessageTypesPerCategoryCount { get; set; } = 10;
	public int MessageSize { get; set; } = 1;
	public int MaxBatchSize { get; set; } = 50;
	public int TotalMessagesCount { get; set; } = 126000000;
	public LoadTestEnvironmentType EnvironmentType { get; set; } = LoadTestEnvironmentType.TestServer;
	public required string KurrentDBConnectionString { get; set; } = "Dummy";
	public required string DuckDbConnectionString { get; set; } = "Dummy";
	public DuckDbTestEnvironmentOptions DuckDb { get; set; } = new();
	public IndexesLoadTestEnvironmentOptions Index { get; set; } = new IndexesLoadTestEnvironmentOptions();


	public LoadTestPartitionConfig[] GeneratePartitions() {
		var categoriesPerPartition = CategoriesCount / PartitionsCount;
		var messagesPerPartition = TotalMessagesCount / PartitionsCount;

		var partitions = new LoadTestPartitionConfig[PartitionsCount];

		for (int i = 0; i < PartitionsCount; i++) {
			var isLastPartition = i == PartitionsCount - 1;

			partitions[i] = new LoadTestPartitionConfig(
				PartitionId: i,
				StartCategoryIndex: i * categoriesPerPartition,
				CategoriesCount: isLastPartition
					? CategoriesCount - i * categoriesPerPartition
					: categoriesPerPartition,
				MaxStreamsPerCategory: MaxStreamsPerCategory,
				MessageTypesCount: MessageTypesPerCategoryCount,
				MessageSize: MessageSize,
				MaxBatchSize: MaxBatchSize,
				TotalMessagesCount: isLastPartition
					? TotalMessagesCount - i * messagesPerPartition
					: messagesPerPartition
			);
		}

		return partitions;
	}
}
