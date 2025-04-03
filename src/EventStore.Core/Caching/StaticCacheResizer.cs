// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using EventStore.Common.Utils;
using Serilog;

namespace EventStore.Core.Caching;

public class StaticCacheResizer : CacheResizer, ICacheResizer {
	private readonly long _capacity;

	public StaticCacheResizer(ResizerUnit unit, long capacity, IDynamicCache cache)
		: base(unit, cache) {
		Ensure.Nonnegative(capacity, nameof(capacity));
		_capacity = capacity;
	}

	public int Weight => 0;

	public long ReservedCapacity => _capacity;

	public void CalcCapacity(long unreservedCapacity, int totalWeight) {
		Cache.SetCapacity(_capacity);
		Log.Debug(
			"{name} statically allotted {capacity:N0} " + Unit,
			Name, Cache.Capacity);
	}

	public void ResetFreedSize() {
		Cache.ResetFreedSize();
	}

	public IEnumerable<CacheStats> GetStats(string parentKey) {
		yield return new CacheStats(BuildStatsKey(parentKey), Name, Cache.Capacity, Size, Count, numChildren: 0);
	}
}
