// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using KurrentDB.Core.Time;

namespace KurrentDB.Projections.Core.Metrics;

public interface IProjectionExecutionTracker {
	public static IProjectionExecutionTracker NoOp => NoOpTracker.Instance;

	void CallExecuted(Instant start, string projectionName, string jsFunctionName);
}

file sealed class NoOpTracker : IProjectionExecutionTracker {
	public static NoOpTracker Instance { get; } = new();

	public void CallExecuted(Instant start, string projectionName, string jsFunctionName) {
	}
}
