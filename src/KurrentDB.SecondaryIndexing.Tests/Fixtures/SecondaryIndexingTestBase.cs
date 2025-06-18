// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Xunit.Abstractions;

namespace KurrentDB.SecondaryIndexing.Tests.Fixtures;

public abstract class SecondaryIndexingTestBase: IAsyncLifetime {
	protected SecondaryIndexingFixture Fixture { get; }

	protected static string RandomStreamName() => $"c{DateTime.Now.Ticks}-{Guid.NewGuid()}";

	protected SecondaryIndexingTestBase(SecondaryIndexingFixture fixture, ITestOutputHelper output) {
		Fixture = fixture;
		Fixture.CaptureTestRun(output);
	}

	public virtual Task InitializeAsync() {
		return Task.CompletedTask;
	}

	public virtual Task DisposeAsync() {
		return Task.CompletedTask;
	}
}
