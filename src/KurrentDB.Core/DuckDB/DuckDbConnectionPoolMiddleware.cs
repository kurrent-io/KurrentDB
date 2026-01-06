// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using Kurrent.Quack.ConnectionPool;

namespace KurrentDB.Core.DuckDB;

/// <summary>
/// Represents a DuckDBConnectionPool scoped to a single kestrel connection
/// </summary>
/// <remarks>
/// The underlying pool is constructed lazily.
/// Dispose must be called after GetPool is no longer being called.
/// </remarks>
public sealed class ConnectionScopedDuckDBConnectionPool(DuckDBConnectionPoolLifetime factory) : IDisposable {
	volatile DuckDBConnectionPool _pool;

	public DuckDBConnectionPool GetPool() {
		if (_pool is not { } pool) {
			pool = factory.CreatePool();
			if (Interlocked.CompareExchange(ref _pool, pool, null) is { } existing) {
				pool.Dispose();
				pool = existing;
			}
		}

		return pool;
	}

	public void Dispose() {
		Interlocked.Exchange(ref _pool, null)?.Dispose();
	}
}
