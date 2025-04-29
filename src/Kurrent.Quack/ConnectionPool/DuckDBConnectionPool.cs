// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using DotNext;
using DotNext.Collections.Concurrent;

namespace Kurrent.Quack.ConnectionPool;

/// <summary>
/// Represents connection pool.
/// </summary>
/// <param name="connectionString">The connection string.</param>
public partial class DuckDBConnectionPool(string connectionString) : Disposable {
	private const int PoolSize = 32; // matches to IndexPool.Capacity

	private ConnectionArray _array;
	private IndexPool _indices = new(PoolSize - 1);

	/// <summary>
	/// Creates a connection that is not in the pool.
	/// </summary>
	/// <returns>An opened connection.</returns>
	public DuckDBAdvancedConnection Open() {
		var connection = new DuckDBAdvancedConnection { ConnectionString = connectionString };
		connection.Open();
		return connection;
	}

	/// <summary>
	/// Rents a new connection.
	/// </summary>
	/// <returns>An opened connection.</returns>
	public Scope Rent(out DuckDBAdvancedConnection connection) {
		Scope scope;
		if (_indices.TryPeek(out var index)) {
			ref var slot = ref _array[index];
			if (slot is null) {
				try {
					slot = new DuckDBAdvancedConnection { ConnectionString = connectionString };
					slot.Open();
				} catch {
					_indices.Return(index);
					throw;
				}
			}

			connection = slot;
			scope = new(this, index);
		} else {
			connection = Open();
			scope = default;
		}

		return scope;
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			Dispose<DuckDBAdvancedConnection?>(_array);
		}

		base.Dispose(disposing);
	}

	[InlineArray(PoolSize)]
	private struct ConnectionArray {
		private DuckDBAdvancedConnection? _connection;
	}
}
