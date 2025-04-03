// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using EventStore.Common.Utils;
using Serilog;

namespace EventStore.Core.Caching;

public class DynamicCacheResizer : CacheResizer, ICacheResizer {
	private readonly long _minCapacity;
	private readonly long _maxCapacity;

	public DynamicCacheResizer(ResizerUnit unit, long minCapacity, long maxCapacity, int weight, IDynamicCache cache)
		: base(unit, cache) {
		Ensure.Positive(weight, nameof(weight));
		Ensure.Nonnegative(minCapacity, nameof(minCapacity));
		Ensure.Nonnegative(maxCapacity, nameof(maxCapacity));

		Weight = weight;
		_minCapacity = minCapacity;
		_maxCapacity = maxCapacity;
	}

	public int Weight { get; }

	public long ReservedCapacity => 0;

	public void CalcCapacity(long unreservedCapacity, int totalWeight) {
		var sw = Stopwatch.StartNew();

		var oldCapacity = Cache.Capacity;
		var capacity = Math.Clamp(
			value: unreservedCapacity.ScaleByWeight(Weight, totalWeight),
			min: _minCapacity,
			max: _maxCapacity);
		Cache.SetCapacity(capacity);

		sw.Stop();

		var diff = capacity - oldCapacity;
		Log.Debug(
			"{name} dynamically allotted {capacity:N0} " + Unit + " ({diff:+#,#;-#,#;+0}). Took {elapsed:N0} ms.",
			Name, Cache.Capacity, diff, sw.ElapsedMilliseconds);
	}

	public void ResetFreedSize() {
		Cache.ResetFreedSize();
	}

	public IEnumerable<CacheStats> GetStats(string parentKey) {
		yield return new CacheStats(BuildStatsKey(parentKey), Name, Cache.Capacity, Size, Count, numChildren: 0);
	}
}
