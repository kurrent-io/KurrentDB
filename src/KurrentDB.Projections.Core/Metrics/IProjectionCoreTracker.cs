// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Time;

namespace KurrentDB.Projections.Core.Metrics;

public delegate void OnJsProjectionExecuted(Instant start, string projection, string jsHandler);

public interface IProjectionCoreTracker {
	public static IProjectionCoreTracker NoOp => NoOpTracker.Instance;
	public OnJsProjectionExecuted OnJsProjectionExecuted { get; }
}

file sealed class NoOpTracker : IProjectionCoreTracker {
	public static NoOpTracker Instance { get; } = new();
	public OnJsProjectionExecuted OnJsProjectionExecuted { get; } = (_, _, _) => { };
}
