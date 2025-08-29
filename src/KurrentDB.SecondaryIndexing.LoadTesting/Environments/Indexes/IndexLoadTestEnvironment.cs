// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.LoadTesting.Assertions;
using KurrentDB.SecondaryIndexing.LoadTesting.Assertions.DuckDb;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.Indexes;

public class IndexesLoadTestEnvironmentOptions {
	public int CommitSize { get; set; } = 50000;
}

public class IndexesLoadTestEnvironment : ILoadTestEnvironment {
	private readonly DuckDBConnectionPool _pool;
	public IMessageBatchAppender MessageBatchAppender { get; }
	public IIndexingSummaryAssertion AssertThat { get; }

	public ValueTask InitializeAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

	public IndexesLoadTestEnvironment() {
		var dbPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!, "index.db");

		if (File.Exists(dbPath))
			File.Delete(dbPath);

		_pool = new($"Data Source={dbPath};");

		MessageBatchAppender = new IndexMessageBatchAppender(_pool, 50000);
		AssertThat = new DuckDbIndexingSummaryAssertion(_pool);
	}

	public ValueTask DisposeAsync() {
		_pool.Dispose();
		return ValueTask.CompletedTask;
	}
}
