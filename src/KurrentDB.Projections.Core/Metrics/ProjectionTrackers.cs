// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Time;

namespace KurrentDB.Projections.Core.Metrics;

public class ProjectionTrackers(
	Func<string, IProjectionExecutionTracker> executionTrackerFactory,
	Func<string, IProjectionStateSerializationTracker> serializationTrackerFactory) {

	public static ProjectionTrackers NoOp { get; } = new(
		_ => IProjectionExecutionTracker.NoOp,
		_ => IProjectionStateSerializationTracker.NoOp);

	public IProjectionExecutionTracker GetExecutionTrackerForProjection(string projectionName) {
		return executionTrackerFactory(projectionName);
	}

	public IProjectionStateSerializationTracker GetSerializationTrackerForProjection(string projectionName) {
		return serializationTrackerFactory(projectionName);
	}
}

public class ProjectionExecutionHistogramTracker(string projectionName, DurationMetric executionDurationMetric) : IProjectionExecutionTracker {
	public void CallExecuted(Instant start, string jsFunctionName) {
		executionDurationMetric.Record(
			start,
			new KeyValuePair<string, object>("projection", projectionName),
			new KeyValuePair<string, object>("jsFunction", jsFunctionName));
	}
}

public class ProjectionExecutionMaxTracker(IDurationMaxTracker tracker) : IProjectionExecutionTracker {
	public void CallExecuted(Instant start, string jsFunctionName) {
		tracker.RecordNow(start);
	}
}

public class CompositeProjectionExecutionTracker(IReadOnlyList<IProjectionExecutionTracker> children)
	: IProjectionExecutionTracker {

	public void CallExecuted(Instant start, string jsFunctionName) {
		foreach (var child in children) {
			child.CallExecuted(start, jsFunctionName);
		}
	}
}
