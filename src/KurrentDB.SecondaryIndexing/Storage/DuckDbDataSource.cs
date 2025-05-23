// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.TransactionLog.Chunks;

namespace KurrentDB.SecondaryIndexing.Storage;

public class DuckDbDataSource : Disposable {
	public readonly DuckDBConnectionPool Pool;
	private readonly string _connectionString;
	private bool _wasInitialized;
	private readonly object _initDbLock = new();

	public DuckDbDataSource(TFChunkDbConfig dbConfig) {
		_connectionString = $"Data Source={Path.Combine(dbConfig.Path, "index.db")};";
		Pool = new DuckDBConnectionPool(_connectionString);
	}

	public void InitDb() {
		lock (_initDbLock) {
			if (_wasInitialized) return;

			using (Pool.Rent(out var connection)) {
				DuckDbSchema.CreateSchema(connection);
			}

			_wasInitialized = true;
		}
	}

	public DuckDBAdvancedConnection OpenNewConnection() {
		var connection = new DuckDBAdvancedConnection { ConnectionString = _connectionString };
		connection.Open();
		return connection;
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			using (Pool.Rent(out var connection)) {
				connection.Checkpoint();
			}
			Pool.Dispose();
		}

		base.Dispose(disposing);
	}

	private struct CheckpointSql : IParameterlessStatement {
		public static ReadOnlySpan<byte> CommandText => "checkpoint"u8;
	}
}
