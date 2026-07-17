// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Security.Claims;
using Kurrent.Quack;
using KurrentDB.Core.Services.Transport.Enumerators;
using KurrentDB.Core.XUnit.Tests;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Tests.Fixtures;

public abstract class DuckDbIntegrationTest<T> : DirectoryPerTest<T> {
	protected readonly DuckDBExecutor Executor;

	protected DuckDbIntegrationTest() {
		var dbPath = Fixture.GetFilePathFor($"{GetType().Name}.db");

		Executor = new($"Data Source={dbPath};", workerCount: 2, dispatcherCount: 2);

		var schema = new IndexingDbSchema(GetEvents);
		Executor.Execute(connection => {
			schema.Execute(connection);
			return 0;
		}, CancellationToken.None).AsTask().GetAwaiter().GetResult();
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
		await Executor.DisposeAsync();
		await base.DisposeAsync();
	}
}
