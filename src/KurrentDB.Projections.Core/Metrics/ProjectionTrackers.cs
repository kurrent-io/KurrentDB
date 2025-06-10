// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace KurrentDB.Projections.Core.Metrics;

public class ProjectionTrackers(Func<string, IProjectionStateSerializationTracker> serializationTrackerFactory) {
	public static ProjectionTrackers NoOp { get; } = new(_ => IProjectionStateSerializationTracker.NoOp);

	public IProjectionStateSerializationTracker GetSerializationTrackerForProjection(string projectionName) {
		return serializationTrackerFactory(projectionName);
	}
}

