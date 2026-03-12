// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Metrics;
using KurrentDB.Core.Time;

namespace KurrentDB.Projections.Core.Metrics;

public class ProjectionStateSerializationTracker(IDurationMaxTracker tracker) : IProjectionStateSerializationTracker {
	public void StateSerialized(Instant start) {
		tracker.RecordNow(start);
	}
}
