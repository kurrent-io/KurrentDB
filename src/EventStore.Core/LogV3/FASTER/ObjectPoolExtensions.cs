// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.DataStructures;

namespace EventStore.Core.LogV3.FASTER;

public static class ObjectPoolExtensions {
	public static Lease<T> Rent<T>(this ObjectPool<T> pool) => new(pool);
}

