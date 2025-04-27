// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Duck.ConnectionPool;

internal sealed class DuckDbPoolingConnection : DuckDbAdvancedConnection {
	private IConnectionPool? pool;
	private int index = -1;

	internal void Bind(IConnectionPool pool, int index) {
		this.pool = pool;
		this.index = index;
	}

	protected override void Dispose(bool disposing) {
		if (pool is not null && index >= 0) {
			pool.Return(this, index);
			pool = null;
			index = -1;
		} else {
			base.Dispose(disposing);
		}
	}
}
