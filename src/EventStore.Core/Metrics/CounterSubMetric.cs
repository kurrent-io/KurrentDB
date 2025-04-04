// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

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
