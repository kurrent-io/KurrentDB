// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Caching;

namespace EventStore.Core.Metrics;

public interface ICacheResourcesTracker {
	public void Register(string cacheKey, ResizerUnit unit, Func<CacheStats> getStats);
}

public class CacheResourcesTracker : ICacheResourcesTracker {
	private readonly CacheResourcesMetrics _metrics;

	public CacheResourcesTracker(CacheResourcesMetrics metrics) {
		_metrics = metrics;
	}

	public void Register(string cache, ResizerUnit unit, Func<CacheStats> getStats) {
		_metrics.Register(cache, unit, getStats);
	}

	public class NoOp : ICacheResourcesTracker {
		public void Register(string cacheKey, ResizerUnit unit, Func<CacheStats> getStats) {
		}
	}
}
