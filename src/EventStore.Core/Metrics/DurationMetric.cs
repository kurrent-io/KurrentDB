// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using EventStore.Core.Time;

namespace EventStore.Core.Metrics;

public class DurationMetric {
	private readonly Histogram<double> _histogram;
	private readonly IClock _clock;

	public DurationMetric(Meter meter, string name, bool legacyNames, IClock clock = null) {
		_clock = clock ?? Clock.Instance;
		if (legacyNames) {
			_histogram = meter.CreateHistogram<double>(name + "-seconds");
		} else {
			_histogram = meter.CreateHistogram<double>(name, "seconds");
		}
	}

	public Duration Start(string durationName) =>
		new(this, durationName, _clock.Now);

	public Instant Record(
		Instant start,
		KeyValuePair<string, object> tag1,
		KeyValuePair<string, object> tag2) {

		var now = _clock.Now;
		var elapsedSeconds = now.ElapsedSecondsSince(start);
		_histogram.Record(elapsedSeconds, tag1, tag2);
		return now;
	}
}
