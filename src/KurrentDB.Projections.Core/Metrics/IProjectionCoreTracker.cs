// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable
using KurrentDB.Projections.Core.Services.Interpreted;

namespace KurrentDB.Projections.Core.Metrics;


public interface IProjectionCoreTracker {
	public static IProjectionCoreTracker NoOp => NoOpTracker.Instance;

	public IJsFunctionCaller MeasureJsCallDuration { get; }
}

file sealed class NoOpTracker : IProjectionCoreTracker {
	public static NoOpTracker Instance { get; } = new();

	public IJsFunctionCaller MeasureJsCallDuration { get => IJsFunctionCaller.Default; }
}
