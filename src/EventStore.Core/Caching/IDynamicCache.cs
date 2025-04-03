// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Caching;

// This has its capacity adjusted by the ICacheResizer
public interface IDynamicCache {
	string Name { get; }
	long Capacity { get; }
	long Size { get; }
	// Number of items in cache
	long Count { get; }
	// Approximate amount freed but not yet garbage collected
	long FreedSize { get; }
	void SetCapacity(long capacity);
	void ResetFreedSize();
	// todo: hits and misses
}
