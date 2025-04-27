// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.InteropServices;

namespace KurrentDB.Duck.ConnectionPool;

public partial class DuckDBConnectionPool {

	/// <summary>
	/// Represents the scope of the rented connection.
	/// </summary>
	[StructLayout(LayoutKind.Auto)]
	public readonly struct Scope : IDisposable {
		private readonly int _index;
		private readonly DuckDBConnectionPool? _pool;

		internal Scope(DuckDBConnectionPool pool, int index) {
			_index = index;
			_pool = pool;
		}

		void IDisposable.Dispose() => _pool?._indices.Return(_index);
	}
}
