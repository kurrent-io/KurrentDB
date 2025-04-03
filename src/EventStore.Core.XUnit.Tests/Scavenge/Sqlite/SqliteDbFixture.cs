// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using System.Threading.Tasks;
using EventStore.Core.DataStructures;
using EventStore.Core.TransactionLog.Chunks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge.Sqlite;

public class SqliteDbFixture<T> : IAsyncLifetime {
	private readonly string _connectionString;

	public SqliteConnection DbConnection { get; set; }
	public ObjectPool<SqliteConnection> DbConnectionPool { get; set; }

	public string Directory { get; }

	public SqliteDbFixture(string dir) {
		Directory = dir;
		var fileName = typeof(T).Name + ".db";
		var connectionStringBuilder = new SqliteConnectionStringBuilder();
		connectionStringBuilder.Pooling = false; // prevents the db files from being locked
		connectionStringBuilder.DataSource = Path.Combine(dir, fileName);
		_connectionString = connectionStringBuilder.ConnectionString;
	}
	
	public Task InitializeAsync() {
		DbConnection = new SqliteConnection(_connectionString);
		DbConnectionPool = new ObjectPool<SqliteConnection>(
			objectPoolName: "sqlite connections",
			initialCount: 0,
			maxCount: TFChunkScavenger.MaxThreadCount + 1,
			factory: () => {
				var dbConnection = new SqliteConnection(_connectionString);
				dbConnection.Open();
				return dbConnection;
			},
			dispose: dbConnection => {
				dbConnection.Close();
				dbConnection.Dispose();
			});
		return DbConnection.OpenAsync();
	}

	public Task DisposeAsync() {
		DbConnection.Close();
		DbConnection.Dispose();
		DbConnectionPool.Dispose();
		return Task.CompletedTask;
	}
}
