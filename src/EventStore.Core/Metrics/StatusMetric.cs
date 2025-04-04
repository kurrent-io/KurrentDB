// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using EventStore.Core.Time;

namespace EventStore.Core.Metrics;

// A metric that tracks the statuses of multiple components.
// We are only expecting to have a handful of components.
public class StatusMetric {
	private readonly List<StatusSubMetric> _subMetrics = new();
	private readonly IClock _clock;

	public StatusMetric(Meter meter, string name, bool legacyNames, IClock clock = null) {
		_clock = clock ?? Clock.Instance;
		if (legacyNames)
			meter.CreateObservableCounter(name, Observe);
		else
			meter.CreateObservableGauge(name, Observe);
	}

	public void Add(StatusSubMetric subMetric) {
		lock (_subMetrics) {
			_subMetrics.Add(subMetric);
		}
	}

	private IEnumerable<Measurement<long>> Observe() {
		var secondsSinceEpoch = _clock.SecondsSinceEpoch;
		lock (_subMetrics) {
			foreach (var instance in _subMetrics) {
				yield return instance.Observe(secondsSinceEpoch);
			}
		}
	}
}
