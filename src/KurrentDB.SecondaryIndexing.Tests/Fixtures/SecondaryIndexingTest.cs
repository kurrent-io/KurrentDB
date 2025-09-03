// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Xunit.Abstractions;

namespace KurrentDB.SecondaryIndexing.Tests.Fixtures;

public abstract class SecondaryIndexingTest<TFixture> : IClassFixture<TFixture> where TFixture : SecondaryIndexingFixture {
	protected TFixture Fixture { get; }

	protected static string RandomStreamName() => $"c{DateTime.Now.Ticks}-{Guid.NewGuid()}";

	protected SecondaryIndexingTest(TFixture fixture, ITestOutputHelper output) {
		Fixture = fixture;
		Fixture.CaptureTestRun(output);
	}
}
