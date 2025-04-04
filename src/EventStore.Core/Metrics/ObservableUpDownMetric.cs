// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace EventStore.Core.Metrics;

public class ObservableUpDownMetric<T> where T : struct {
	private readonly List<Func<Measurement<T>>> _measurementProviders = new();
	private readonly object _lock = new();

	public ObservableUpDownMetric(Meter meter, string name, string unit = null) {
		meter.CreateObservableUpDownCounter(name, Observe, unit);
	}

	public void Register(Func<Measurement<T>> measurementProvider) {
		if (measurementProvider is null) {
			throw new ArgumentException("Measurement provider couldn't be null");
		}

		lock (_lock) {
			_measurementProviders.Add(measurementProvider);
		}
	}

	private IEnumerable<Measurement<T>> Observe() {
		lock (_lock) {
			foreach (var measurementProvider in _measurementProviders) {
				yield return measurementProvider();
			}
		}
	}
}
