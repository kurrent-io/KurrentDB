// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.LoadTesting.Assertions;
using KurrentDB.SecondaryIndexing.LoadTesting.Assertions.DuckDb;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.DuckDB;

public enum DuckDBClientType {
	Duck,
	Quack
}

public class DuckDBTestEnvironmentOptions {
	public DuckDBClientType ClientType { get; set; } = DuckDBClientType.Quack;
	public int CommitSize { get; set; } = 50000;
	public string? WalAutocheckpoint { get; set; }
	public int? CheckpointSize { get; set; }
}

public class DuckDBTestEnvironment : ILoadTestEnvironment {
	private readonly DuckDbDataSource _dataSource;
	public IMessageBatchAppender MessageBatchAppender { get; }
	public IIndexingSummaryAssertion AssertThat { get; }

	public DuckDBTestEnvironment(DuckDBTestEnvironmentOptions options) {
		var dbPath = Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!, "index.db");

		if (File.Exists(dbPath))
			File.Delete(dbPath);

		_dataSource = new DuckDbDataSource(
			new DuckDbDataSourceOptions { ConnectionString = $"Data Source={dbPath};" }
		);

		MessageBatchAppender = options.ClientType == DuckDBClientType.Duck
			? new RawDuckDbMessageBatchAppender(_dataSource, options)
			: new RawQuackMessageBatchAppender(_dataSource, options);

		AssertThat = new DuckDbIndexingSummaryAssertion(_dataSource);
	}

	public ValueTask InitializeAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

	public async ValueTask DisposeAsync() {
		await MessageBatchAppender.DisposeAsync();
		_dataSource.Dispose();
	}
}
