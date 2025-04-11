// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.IO;
using Dapper;
using DuckDB.NET.Data;
using EventStore.Core.TransactionLog.Chunks;

namespace EventStore.Core.Duck;

public class DuckDb(TFChunkDbConfig dbConfig) {
	private readonly string _connectionString = $"Data Source={Path.Combine(dbConfig.Path, "index.db")};";
	private readonly ConcurrentBag<DuckDBConnection> _connectionPool = new();

	public void InitDb() {
		using var connection = OpenConnection();
		DuckDbSchema.CreateSchema(connection);
	}

	public DuckDBConnection OpenConnection() {
		var connection = new DuckDBConnection(_connectionString);
		connection.Open();
		return connection;
	}

	public DuckDBConnection GetOrOpenConnection() {
		if (!_connectionPool.TryTake(out var connection)) {
			connection = new(_connectionString);
			connection.Open();

			Debug.Assert(connection.State is ConnectionState.Open);
		}

		return connection;
	}

	public void ReturnConnection(DuckDBConnection connection) {
		Debug.Assert(connection.State is ConnectionState.Open);

		_connectionPool.Add(connection);
	}

	public void Close() {
		using (var connection = OpenConnection()) {
			connection.Execute("checkpoint");
		}

		while (_connectionPool.TryTake(out var connection)) {
			connection.Dispose();
		}
	}
}
