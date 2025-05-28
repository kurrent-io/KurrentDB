// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Xunit.Abstractions;

namespace KurrentDB.SecondaryIndexing.Tests.Fixtures;

public abstract class SecondaryIndexingTestBase {
	protected SecondaryIndexingFixture Fixture { get; }

	protected const string CategoryName = "testCategory";

	protected static string RandomStreamName() => $"{CategoryName}-{Guid.NewGuid()}";

	protected SecondaryIndexingTestBase(SecondaryIndexingFixture fixture, ITestOutputHelper output) {
		Fixture = fixture;
		Fixture.CaptureTestRun(output);
	}
}
