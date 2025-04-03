// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Caching;

namespace EventStore.Core.Tests.Caching;

public class EmptyDynamicCache : IDynamicCache {
	public static EmptyDynamicCache Instance { get; } = new();
	public string Name => nameof(EmptyDynamicCache);
	public long Capacity { get; private set; }

	public void SetCapacity(long capacity) {
		Capacity = capacity;
	}

	public void ResetFreedSize() {
	}

	public long Size => 0;
	public long Count => 0;
	public long FreedSize => 0;
}
