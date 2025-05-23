// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Xunit.Abstractions;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests.Fixtures;

public abstract class SecondaryIndexingPluginIntegrationTest {
	protected SecondaryIndexingFixture Fixture { get; }

	protected const string CategoryName = "testCategory";

	protected static string RandomStreamName() => $"${CategoryName}-{Guid.NewGuid()}";

	protected SecondaryIndexingPluginIntegrationTest(SecondaryIndexingFixture fixture, ITestOutputHelper output) {
		Fixture = fixture;
		Fixture.CaptureTestRun(output);
	}
}
