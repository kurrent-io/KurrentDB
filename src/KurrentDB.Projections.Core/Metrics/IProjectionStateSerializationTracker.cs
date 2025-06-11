// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Time;

namespace KurrentDB.Projections.Core.Metrics;

public interface IProjectionStateSerializationTracker {
	public static IProjectionStateSerializationTracker NoOp { get; } = NoOpTracker.Instance;

	public void StateSerialized(Instant start);
}

file sealed class NoOpTracker : IProjectionStateSerializationTracker {
	public static NoOpTracker Instance { get; } = new();

	public void StateSerialized(Instant start) {
	}
}
