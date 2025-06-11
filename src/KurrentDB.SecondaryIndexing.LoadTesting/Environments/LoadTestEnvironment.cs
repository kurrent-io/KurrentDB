// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.LoadTesting.Environments.DuckDB;
using KurrentDB.SecondaryIndexing.LoadTesting.Environments.InMemory;
using KurrentDB.SecondaryIndexing.LoadTesting.Environments.TestServer;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments;

public interface ILoadTestEnvironment {
	IMessageBatchAppender MessageBatchAppender { get; }
}

public enum LoadTestEnvironmentType {
	InMemory,
	TestServer,
	DuckDB,
	Quack,
	Container
}

public static class LoadTestEnvironment {
	public static ILoadTestEnvironment For(LoadTestEnvironmentType loadTestEnvironmentType) =>
		loadTestEnvironmentType switch {
			LoadTestEnvironmentType.InMemory => new InMemoryLoadTestEnvironment(),
			LoadTestEnvironmentType.TestServer => new TestServerEnvironment(),
			LoadTestEnvironmentType.Container => throw new NotImplementedException("Container environment is not yet supported"),
			LoadTestEnvironmentType.DuckDB => new DuckDBTestEnvironment(DuckDBClientType.Duck),
			LoadTestEnvironmentType.Quack => new DuckDBTestEnvironment(DuckDBClientType.Quack),
			_ => throw new ArgumentOutOfRangeException(nameof(loadTestEnvironmentType), loadTestEnvironmentType, null)
		};
}
