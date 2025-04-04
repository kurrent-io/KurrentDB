// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Services.Storage.ReaderIndex;

public class ReadCacheStats {
	public readonly long Size;
	public readonly int Count;
	public readonly long HitCount;
	public readonly long MissCount;

	public ReadCacheStats(long size, int count, long hitCount, long missCount) {
		Size = size;
		Count = count;
		HitCount = hitCount;
		MissCount = missCount;
	}
}
