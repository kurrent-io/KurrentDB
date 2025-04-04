// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace EventStore.Core.Metrics;

public class CounterMetric {
	private readonly List<CounterSubMetric> _subMetrics = new();
	private readonly object _lock = new();

	public CounterMetric(Meter meter, string name, string unit, bool legacyNames) {
		if (legacyNames) {
			if (!string.IsNullOrWhiteSpace(unit)) {
				name = name + "-" + unit;
			}

			meter.CreateObservableCounter(name, Observe);
		} else {
			meter.CreateObservableCounter(name, Observe, unit);
		}
	}

	public void Add(CounterSubMetric subMetric) {
		lock (_lock) {
			_subMetrics.Add(subMetric);
		}
	}

	private IEnumerable<Measurement<long>> Observe() {
		lock (_lock) {
			foreach (CounterSubMetric subMetric in _subMetrics) {
				yield return subMetric.Observe();
			}
		}
	}
}
