// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.SecondaryIndexing.Tests.Observability;

namespace KurrentDB.SecondaryIndexing.LoadTesting.Assertions;

public interface IIndexingSummaryAssertion {
	ValueTask IndexesMatch(IndexingSummary summary);
}

public class DummyIndexingSummaryAssertion : IIndexingSummaryAssertion {
	public ValueTask IndexesMatch(IndexingSummary summary) => ValueTask.CompletedTask;
}
