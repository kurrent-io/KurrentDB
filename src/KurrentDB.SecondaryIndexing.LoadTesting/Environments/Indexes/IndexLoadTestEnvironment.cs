// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.Indexes;

public class IndexesLoadTestEnvironmentOptions {
	public int CommitSize { get; set; } = 50000;
}

public class IndexesLoadTestEnvironment: ILoadTestEnvironment {
	public IMessageBatchAppender MessageBatchAppender { get; }

	public IndexesLoadTestEnvironment(IndexesLoadTestEnvironmentOptions options) {
		var dbPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!, "index.db");

		if (File.Exists(dbPath))
			File.Delete(dbPath);

		var dataSource = new DuckDbDataSource(
			new DuckDbDataSourceOptions { ConnectionString = $"Data Source={dbPath};" }
		);

		MessageBatchAppender = new IndexMessageBatchAppender(dataSource, 50000);
	}
}
