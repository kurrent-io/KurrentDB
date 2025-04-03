// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using static EventStore.Common.Configuration.MetricsConfiguration;

namespace EventStore.Core.Metrics;

public interface ICacheHitsMissesTracker {
	void Register(Cache cache, Func<long> getHits, Func<long> getMisses);
}

public class CacheHitsMissesTracker : ICacheHitsMissesTracker {
	private readonly CacheHitsMissesMetric _metric;

	public CacheHitsMissesTracker(CacheHitsMissesMetric metric) {
		_metric = metric;
	}

	public void Register(Cache cache, Func<long> getHits, Func<long> getMisses) =>
		_metric.Register(cache, getHits, getMisses);

	public class NoOp : ICacheHitsMissesTracker {
		public void Register(Cache cache, Func<long> getHits, Func<long> getMisses) {
		}
	}
}
