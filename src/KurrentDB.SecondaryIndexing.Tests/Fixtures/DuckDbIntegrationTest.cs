// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.XUnit.Tests;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Tests.Fixtures;

public abstract class DuckDbIntegrationTest<T> : DirectoryPerTest<T> {
	protected readonly DuckDBConnectionPool DuckDb;

	protected DuckDbIntegrationTest() {
		var dbPath = Fixture.GetFilePathFor($"{GetType().Name}.db");

		DuckDb = new($"Data Source={dbPath};");
		var schema = new IndexingDbSchema();
		schema.CreateSchema(DuckDb);
	}

	public override async Task DisposeAsync() {
		DuckDb.Dispose();
		await base.DisposeAsync();
	}
}
