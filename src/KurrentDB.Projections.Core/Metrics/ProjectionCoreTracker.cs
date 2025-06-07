// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using KurrentDB.Core.Metrics;
using KurrentDB.Core.Time;
using KurrentDB.Projections.Core.Services.Interpreted;

namespace KurrentDB.Projections.Core.Metrics;

public class ProjectionCoreTracker(DurationMetric executionDurationMetric) : IProjectionCoreTracker {
	public void CallExecuted(Instant start, string projectionName, string jsFunctionName) {
		executionDurationMetric.Record(
			start,
			new KeyValuePair<string, object>("projection", projectionName),
			new KeyValuePair<string, object>("jsFunction", jsFunctionName));
	}
}
