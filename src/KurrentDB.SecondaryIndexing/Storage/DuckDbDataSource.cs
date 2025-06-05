// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using DotNext.Threading;
using DuckDB.NET.Data;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.TransactionLog.Chunks;

namespace KurrentDB.SecondaryIndexing.Storage;

public class DuckDbDataSource : Disposable {
	readonly string _connectionString;

	public readonly DuckDBConnectionPool Pool;
	private Atomic.Boolean _wasInitialized;

	public DuckDbDataSource(TFChunkDbConfig dbConfig) {
		_connectionString = $"Data Source={Path.Combine(dbConfig.Path, "index.db")};";
		Pool = new(_connectionString);
	}

	public void InitDb() {
		if (_wasInitialized.FalseToTrue()) {
			using var connection = OpenConnection();
			DuckDbSchema.CreateSchema(connection);
		}
	}

	public DuckDBConnection OpenConnection() {
		var connection = new DuckDBConnection(_connectionString);
		connection.Open();
		return connection;
	}

	public DuckDBAdvancedConnection OpenNewConnection() => Pool.Open();
	// public DuckDBAdvancedConnection OpenNewConnection() => ;

	protected override void Dispose(bool disposing) {
		if (disposing) {
			using var connection = OpenConnection();
			// using (Pool.Rent(out var connection)) {
			connection.Checkpoint();
			// }
			Pool.Dispose();
		}

		base.Dispose(disposing);
	}
}
