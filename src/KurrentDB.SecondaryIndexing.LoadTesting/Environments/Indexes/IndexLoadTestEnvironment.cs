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
	private readonly DuckDbDataSource _dataSource;
	public IMessageBatchAppender MessageBatchAppender { get; }
	public ValueTask InitAsync(CancellationToken ct = default) {
		throw new NotImplementedException();
	}

	public IndexesLoadTestEnvironment(IndexesLoadTestEnvironmentOptions options) {
		var dbPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!, "index.db");

		if (File.Exists(dbPath))
			File.Delete(dbPath);

		_dataSource = new DuckDbDataSource(
			new DuckDbDataSourceOptions { ConnectionString = $"Data Source={dbPath};" }
		);

		MessageBatchAppender = new IndexMessageBatchAppender(_dataSource, 50000);
	}

	public ValueTask DisposeAsync() {
		_dataSource.Dispose();
		return ValueTask.CompletedTask;
	}
}
