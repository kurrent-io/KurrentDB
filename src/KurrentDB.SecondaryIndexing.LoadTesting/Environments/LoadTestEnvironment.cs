// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.LoadTesting.Assertions;
using KurrentDB.SecondaryIndexing.LoadTesting.Environments.DuckDB;
using KurrentDB.SecondaryIndexing.LoadTesting.Environments.gRPC;
using KurrentDB.SecondaryIndexing.LoadTesting.Environments.Indexes;
using KurrentDB.SecondaryIndexing.LoadTesting.Environments.InMemory;
using KurrentDB.SecondaryIndexing.LoadTesting.Environments.TestServer;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments;

public interface ILoadTestEnvironment: IAsyncDisposable {
	IMessageBatchAppender MessageBatchAppender { get; }
	IIndexingSummaryAssertion AssertThat { get; }
	public ValueTask InitializeAsync(CancellationToken ct = default);
}

public enum LoadTestEnvironmentType {
	InMemory,
	TestServer,
	Index,
	DuckDb,
	gRPC
}

public static class LoadTestEnvironment {
	public static ILoadTestEnvironment For(LoadTestConfig config) =>
		config.EnvironmentType switch {
			LoadTestEnvironmentType.InMemory => new InMemoryLoadTestEnvironment(),
			LoadTestEnvironmentType.TestServer => new TestServerEnvironment(),
			LoadTestEnvironmentType.gRPC => new gRPCClientEnvironment(config.KurrentDBConnectionString),
			LoadTestEnvironmentType.DuckDb => new DuckDbTestEnvironment(config.DuckDb),
			LoadTestEnvironmentType.Index => new IndexesLoadTestEnvironment(),
			_ => throw new ArgumentOutOfRangeException(nameof(config.EnvironmentType), config.EnvironmentType, null)
		};
}
