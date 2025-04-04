// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

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
