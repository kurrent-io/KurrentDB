// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.DataStructures;

namespace EventStore.Core.LogV3.FASTER;

public struct Lease<T> : IDisposable {
	private readonly ObjectPool<T> _pool;
	public T Reader { get; }

	public Lease(ObjectPool<T> pool) {
		_pool = pool;
		Reader = _pool.Get();
	}
	public void Dispose() {
		_pool.Return(Reader);
	}
}

