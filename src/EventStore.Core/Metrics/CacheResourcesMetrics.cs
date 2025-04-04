// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using EventStore.Core.Caching;

namespace EventStore.Core.Metrics;

public class CacheResourcesMetrics {
	private readonly ObservableUpDownMetric<long> _bytesMetric;
	private readonly ObservableUpDownMetric<long> _entriesMetric;

	public CacheResourcesMetrics(Meter meter, string name, bool legacyNames) {
		_bytesMetric = new ObservableUpDownMetric<long>(meter, $"{name}-bytes", legacyNames ? null : "bytes");
		_entriesMetric = new ObservableUpDownMetric<long>(meter, $"{name}-entries", legacyNames ? null : "entries");
	}

	public void Register(string cache, ResizerUnit unit, Func<CacheStats> getStats) {
		var sizeAndCapacityMetric = unit == ResizerUnit.Entries
			? _entriesMetric
			: _bytesMetric;

		Register(sizeAndCapacityMetric, cache, "capacity", () => getStats().Capacity);
		Register(sizeAndCapacityMetric, cache, "size", () => getStats().Size);
		Register(_entriesMetric, cache, "count", () => getStats().Count);
	}

	private static void Register(
		ObservableUpDownMetric<long> metric,
		string cache,
		string metricName,
		Func<long> measurementProvider) {

		var tags = new KeyValuePair<string, object>[] {
			new("cache", cache),
			new("kind", metricName)
		};
		metric.Register(() => new(measurementProvider(), tags));
	}
}
