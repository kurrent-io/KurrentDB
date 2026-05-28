// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Services;

namespace KurrentDB.Projections.Core.Metrics;

public interface IProjectionTracker {
	void OnNewStats(ProjectionStatistics[] newStats);

	public static IProjectionTracker NoOp => NoOpTracker.Instance;
}

file sealed class NoOpTracker : IProjectionTracker {
	public static NoOpTracker Instance { get; } = new();

	public void OnNewStats(ProjectionStatistics[] newStats) { }
}
