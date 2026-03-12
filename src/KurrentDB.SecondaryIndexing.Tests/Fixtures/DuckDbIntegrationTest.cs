// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using Kurrent.Quack.ConnectionPool;
using Kurrent.Quack.Threading;
using KurrentDB.Core.DuckDB;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.XUnit.Tests;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Tests.Fixtures;

public abstract class DuckDbIntegrationTest<T> : DirectoryPerTest<T> {
	protected readonly DuckDBConnectionPool DuckDb;

	protected DuckDbIntegrationTest() {
		var dbPath = Fixture.GetFilePathFor($"{GetType().Name}.db");

		DuckDb = new($"Data Source={dbPath};");
		var schema = new IndexingDbSchema();
		using (DuckDb.Rent(out var connection)) {
			schema.Execute(connection);
			KdbGetEventSetup.Register(connection, GetEvents);
		}
	}

	private static IEnumerator<ReadResponse> GetEvents(long[] logPositions, ClaimsPrincipal user) {
		// This is stub method for tests
		for (var i = 0; i < logPositions.Length; i++) {
			// See GetDatabaseEventsFunction implementation,
			// for any unexpected response the function generates a row with empty values
			yield return new ReadResponse.StreamNotFound(string.Empty);
		}
	}

	public override async ValueTask DisposeAsync() {
		DuckDb.Dispose();
		await base.DisposeAsync();
	}
}
