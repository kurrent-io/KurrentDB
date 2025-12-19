// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Kurrent.Quack.ConnectionPool;

namespace KurrentDB.Core.DuckDB;

public class ConnectionScopedDuckDBConnectionPool(Lazy<DuckDBConnectionPool> poolFactory) {
	public DuckDBConnectionPool Pool {
		get {
			// synchronize with disposal
			lock (poolFactory) {
				return poolFactory.Value;
			}
		}
	}
}
