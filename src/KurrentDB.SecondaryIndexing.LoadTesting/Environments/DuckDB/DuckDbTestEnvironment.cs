// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.DuckDB;

public enum DuckDBClientType {
	Duck,
	Quack
}

public class DuckDBTestEnvironment : ILoadTestEnvironment {
	public IMessageBatchAppender MessageBatchAppender { get; }

	public DuckDBTestEnvironment(DuckDBClientType duckDBClientType) {
		var dbPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!, "index.db");

		if (File.Exists(dbPath))
			File.Delete(dbPath);

		var dataSource = new DuckDbDataSource(
			new DuckDbDataSourceOptions { ConnectionString = $"Data Source={dbPath};" }
		);

		MessageBatchAppender = duckDBClientType == DuckDBClientType.Duck
			? new RawDuckDbMessageBatchAppender(dataSource, 50000)
			: new RawQuackMessageBatchAppender(dataSource, 50000);
	}
}
