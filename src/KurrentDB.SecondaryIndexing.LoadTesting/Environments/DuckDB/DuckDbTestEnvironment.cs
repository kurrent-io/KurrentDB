// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;
using KurrentDB.SecondaryIndexing.LoadTesting.Appenders;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Environments.DuckDB;

public class DuckDBTestEnvironment : ILoadTestEnvironment {
	public IMessageBatchAppender MessageBatchAppender { get; } = new RawDuckDbMessageBatchAppender(
		new DuckDbDataSource(
			new DuckDbDataSourceOptions {
				ConnectionString =
					$"Data Source={Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!, "index.db")};"
			}
		),
		50000
	);
}
