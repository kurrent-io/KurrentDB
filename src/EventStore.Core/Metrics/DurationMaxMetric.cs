// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace EventStore.Core.Metrics;

public class DurationMaxMetric {
	private readonly List<DurationMaxTracker> _trackers = new();

	public DurationMaxMetric(Meter meter, string name, bool legacyNames) {
		// gauge rather than updowncounter because the dimensions wont make sense to sum,
		// because they are maxes and not necessarily from the same moment
		if (legacyNames) {
			meter.CreateObservableGauge(name + "-seconds", Observe);
		} else {
			meter.CreateObservableGauge(name, Observe, "seconds");
		}
	}

	public void Add(DurationMaxTracker tracker) {
		lock (_trackers) {
			_trackers.Add(tracker);
		}
	}

	private IEnumerable<Measurement<double>> Observe() {
		lock (_trackers) {
			foreach (var tracker in _trackers) {
				yield return tracker.Observe();
			}
		}
	}
}
