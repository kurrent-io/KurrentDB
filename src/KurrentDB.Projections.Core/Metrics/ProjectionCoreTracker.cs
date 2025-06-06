// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Metrics;
using KurrentDB.Projections.Core.Services.Interpreted;

namespace KurrentDB.Projections.Core.Metrics;

public class ProjectionCoreTracker(IDurationMetric executionDurationMetric) : IProjectionCoreTracker {
	public IJsFunctionCaller MeasureJsCallDuration { get; } = new JsFunctionCallMeasurer(executionDurationMetric);
}
