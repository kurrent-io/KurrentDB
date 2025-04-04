// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Metrics;


public interface IDurationTracker {
	Duration Start();
}

public class DurationTracker : IDurationTracker {
	private readonly DurationMetric _metric;
	private readonly string _durationName;

	public DurationTracker(DurationMetric metric, string durationName) {
		_metric = metric;
		_durationName = durationName;
	}

	public Duration Start() => _metric.Start(_durationName);

	public class NoOp : IDurationTracker {
		public Duration Start() => Duration.Nil;
	}
}
