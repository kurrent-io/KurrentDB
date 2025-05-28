// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Xunit.Abstractions;

namespace KurrentDB.SecondaryIndexing.Tests.IntegrationTests.Fixtures;

public abstract class SecondaryIndexingPluginIntegrationTest {
	protected SecondaryIndexingFixture Fixture { get; }

	protected static string RandomStreamName() => $"test-{Guid.NewGuid()}";

	protected static string IndexStreamName => SecondaryIndexingFixture.IndexStreamName;

	protected SecondaryIndexingPluginIntegrationTest(SecondaryIndexingFixture fixture, ITestOutputHelper output) {
		Fixture = fixture;
		Fixture.CaptureTestRun(output);
	}
}
