// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using KurrentDB.Core.Metrics;

namespace KurrentDB.Projections.Core.Metrics;

public class ProjectionCoreTracker(DurationMetric executionDurationMetric) : IProjectionCoreTracker {
	public OnJsProjectionExecuted OnJsProjectionExecuted { get; } = (start, projection, jsHandler) => {
		executionDurationMetric.Record(start,
			new KeyValuePair<string, object>("projection", projection),
			new KeyValuePair<string, object>("jsHandler", jsHandler));
	};
}
