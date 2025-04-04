// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using Xunit;

namespace EventStore.Core.XUnit.Tests;

public class DirectoryPerTest<T> : IAsyncLifetime {
	protected DirectoryFixture<T> Fixture { get; private set; } = new();

	public virtual async Task InitializeAsync() {
		await Fixture.InitializeAsync();
	}

	public virtual async Task DisposeAsync() {
		await Fixture.DisposeAsync();
	}
}
