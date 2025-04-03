// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Scavenge.Sqlite;

public class SqliteDbPerTest<T> : IAsyncLifetime {
	protected SqliteDbFixture<T> Fixture { get; }
	private DirectoryFixture<T> DirFixture { get; }

	public SqliteDbPerTest() {
		DirFixture = new DirectoryFixture<T>();
		Fixture = new SqliteDbFixture<T>(DirFixture.Directory);
	}
	
	public async Task InitializeAsync() {
		await DirFixture.InitializeAsync();
		await Fixture.InitializeAsync();
	}

	public async Task DisposeAsync() {
		await Fixture.DisposeAsync();
		await DirFixture.DisposeAsync();
	}
}
