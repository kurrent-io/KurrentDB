// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using DotNext.Threading;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;

namespace KurrentDB.SecondaryIndexing.Storage;

public class DuckDbDataSourceOptions {
	public required string ConnectionString { get; set; }
}

public class DuckDbDataSource : Disposable {
	public readonly DuckDBConnectionPool Pool;
	Atomic.Boolean _wasInitialized;

	public DuckDbDataSource(DuckDbDataSourceOptions options) {
		var connectionString = options.ConnectionString;
		Pool = new(connectionString);
	}

	public void InitDb() {
		if (!_wasInitialized.FalseToTrue()) return;

		using var connection = OpenNewConnection();
		DuckDbSchema.CreateSchema(connection);
	}

	public DuckDBAdvancedConnection OpenNewConnection() => Pool.Open();

	protected override void Dispose(bool disposing) {
		if (disposing) {
			using var connection = OpenNewConnection();
			connection.Checkpoint();
			Pool.Dispose();
		}

		base.Dispose(disposing);
	}
}
