// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using DotNext;
using DotNext.Collections.Concurrent;

namespace KurrentDB.Duck.ConnectionPool;

/// <summary>
/// Represents connection pool.
/// </summary>
/// <param name="connectionString">The connection string.</param>
public class DuckDBConnectionPool(string connectionString) : Disposable, IConnectionPool {
	private const int PoolSize = 32; // matches to IndexPool.Capacity

	private ConnectionArray _array;
	private IndexPool _pool = new(PoolSize - 1);

	/// <summary>
	/// Creates a connection that is not in the pool.
	/// </summary>
	/// <returns>An opened connection.</returns>
	public DuckDbAdvancedConnection Open() {
		var connection = new DuckDbAdvancedConnection { ConnectionString = connectionString };
		connection.Open();
		return connection;
	}

	/// <summary>
	/// Rents a new connection.
	/// </summary>
	/// <returns>An opened connection.</returns>
	public DuckDbAdvancedConnection Rent() {
		if (_pool.TryPeek(out var index)) {
			ref var connection = ref _array[index];
			if (connection is null) {
				try {
					connection = new DuckDbPoolingConnection { ConnectionString = connectionString };
					connection.Open();
				} catch {
					_pool.Return(index);
					throw;
				}
			}

			connection.Bind(this, index);
			return connection;
		}

		return Open();
	}

	void IConnectionPool.Return(DuckDbPoolingConnection connection, int index) {
		_array[index] = connection;

		// implicit barrier
		_pool.Return(index);
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			Dispose<DuckDbPoolingConnection?>(_array);
		}

		base.Dispose(disposing);
	}

	[InlineArray(PoolSize)]
	private struct ConnectionArray {
		private DuckDbPoolingConnection? _connection;
	}
}
