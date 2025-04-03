// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;

namespace EventStore.Core.Metrics;

public class CounterSubMetric {
	private readonly KeyValuePair<string, object>[] _tags;
	private long _counter;

	public CounterSubMetric(CounterMetric metric, KeyValuePair<string, object>[] tags) {
		_tags = tags;
		metric.Add(this);
	}
	
	public void Add(long delta) {
		Interlocked.Add(ref _counter, delta);
	}

	public Measurement<long> Observe() {
		return new Measurement<long>(Interlocked.Read(ref _counter), _tags);
	}
}
