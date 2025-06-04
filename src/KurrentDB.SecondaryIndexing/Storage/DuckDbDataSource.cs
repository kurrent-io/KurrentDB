// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using DotNext.Threading;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.TransactionLog.Chunks;

namespace KurrentDB.SecondaryIndexing.Storage;

public class DuckDbDataSource(TFChunkDbConfig dbConfig) : Disposable {
	public readonly DuckDBConnectionPool Pool = new($"Data Source={Path.Combine(dbConfig.Path, "index.db")};");
	private Atomic.Boolean _wasInitialized;

	public void InitDb() {
		if (_wasInitialized.FalseToTrue()) {
			using var connection = OpenNewConnection();
			DuckDbSchema.CreateSchema(connection);
		}
	}

	public DuckDBAdvancedConnection OpenNewConnection() => Pool.Open();

	protected override void Dispose(bool disposing) {
		if (disposing) {
			using (Pool.Rent(out var connection)) {
				connection.Checkpoint();
			}
			Pool.Dispose();
		}

		base.Dispose(disposing);
	}
}
