// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.LoadTesting.Assertions;
using KurrentDB.SecondaryIndexing.LoadTesting.Assertions.DuckDb;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.DuckDB;

public class DuckDbTestEnvironmentOptions {
	public int CommitSize { get; set; } = 50000;
	public string? WalAutoCheckpoint { get; set; }
	public int? CheckpointSize { get; set; }
}

public class DuckDbTestEnvironment : ILoadTestEnvironment {
	private readonly DuckDbDataSource _dataSource;
	public IMessageBatchAppender MessageBatchAppender { get; }
	public IIndexingSummaryAssertion AssertThat { get; }

	public DuckDbTestEnvironment(DuckDbTestEnvironmentOptions options) {
		var dbPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!, "index.db");

		if (File.Exists(dbPath))
			File.Delete(dbPath);

		_dataSource = new(new() { ConnectionString = $"Data Source={dbPath};" });

		MessageBatchAppender = new RawQuackMessageBatchAppender(_dataSource, options);

		AssertThat = new DuckDbIndexingSummaryAssertion(_dataSource);
	}

	public ValueTask InitializeAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

	public async ValueTask DisposeAsync() {
		await MessageBatchAppender.DisposeAsync();
		_dataSource.Dispose();
	}
}
