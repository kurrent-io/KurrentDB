// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.LoadTesting.Assertions;
using KurrentDB.SecondaryIndexing.LoadTesting.Assertions.DuckDb;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.Indexes;

public class IndexesLoadTestEnvironmentOptions {
	public int CommitSize { get; set; } = 50000;
}

public class IndexesLoadTestEnvironment : ILoadTestEnvironment {
	private readonly DuckDbDataSource _dataSource;
	public IMessageBatchAppender MessageBatchAppender { get; }
	public IIndexingSummaryAssertion AssertThat { get; }
	public ValueTask InitializeAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

	public IndexesLoadTestEnvironment() {
		var dbPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!, "index.db");

		if (File.Exists(dbPath))
			File.Delete(dbPath);

		_dataSource = new(new() { ConnectionString = $"Data Source={dbPath};" });

		MessageBatchAppender = new IndexMessageBatchAppender(_dataSource, 50000);
		AssertThat = new DuckDbIndexingSummaryAssertion(_dataSource);
	}

	public ValueTask DisposeAsync() {
		_dataSource.Dispose();
		return ValueTask.CompletedTask;
	}
}
