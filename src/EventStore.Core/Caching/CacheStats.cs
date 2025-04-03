// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Caching;

public struct CacheStats {
	public string Key { get; }
	public string Name { get; }
	public long Capacity { get; }
	public long Size { get; }
	public long Count { get; }
	public int NumChildren { get; }
	public double UtilizationPercent => Capacity != 0 ? 100.0 * Size / Capacity : 0;

	public CacheStats(string key, string name, long capacity, long size, long count, int numChildren) {
		Key = key;
		Name = name;
		Capacity = capacity;
		Size = size;
		Count = count;
		NumChildren = numChildren;
	}
}
