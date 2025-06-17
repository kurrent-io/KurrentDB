// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Reflection;
using KurrentDB.Core.Tests;
using KurrentDB.SecondaryIndexing.Storage;

namespace KurrentDB.SecondaryIndexing.Tests.Fixtures;

public abstract class DuckDbIntegrationTest : IAsyncLifetime {
	protected readonly DuckDbDataSource DuckDb;
	private readonly string _directory;

	protected DuckDbIntegrationTest() {
		_directory = Path.Combine(Path.GetTempPath(), GetType().Name);

		if (!Directory.Exists(_directory))
			Directory.CreateDirectory(_directory);

		var dbPath = Path.Combine(_directory, $"{GetType().Name}.db");

		if (File.Exists(dbPath))
			File.Delete(dbPath);

		DuckDb = new DuckDbDataSource(
			new DuckDbDataSourceOptions { ConnectionString = $"Data Source={dbPath};" }
		);
	}

	public virtual Task InitializeAsync() =>
		Task.CompletedTask;

	public virtual Task DisposeAsync() {
		DuckDb.Dispose();
		DirectoryDeleter.TryForceDeleteDirectory(_directory);
		return Task.CompletedTask;
	}
}
