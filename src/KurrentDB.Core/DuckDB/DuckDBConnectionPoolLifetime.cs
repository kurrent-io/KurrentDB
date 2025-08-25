// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using DotNext;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.DuckDB;

namespace KurrentDB.Core.DuckDB;

public class DuckDBConnectionPoolLifetime(TFChunkDbConfig config, IEnumerable<IDuckDBInlineFunction> inlineFunctions) : Disposable {
	private readonly DuckDBConnectionPool _pool = new ConnectionPoolWithFunctions($"Data Source={config.Path}/kurrent.ddb", inlineFunctions.ToArray());

	public DuckDBConnectionPool GetConnectionPool() => _pool;

	protected override void Dispose(bool disposing) {
		if (disposing) {
			using (var connection = _pool.Open()) {
				connection.Checkpoint();
			}

			_pool.Dispose();
		}

		base.Dispose(disposing);
	}

	private class ConnectionPoolWithFunctions(string connectionString, IDuckDBInlineFunction[] inlineFunctions) : DuckDBConnectionPool(connectionString) {
		[Experimental("DuckDBNET001")]
		protected override void Initialize(DuckDBAdvancedConnection connection) {
			base.Initialize(connection);
			for (var i = 0; i < inlineFunctions.Length; i++) {
				try {
					inlineFunctions[i].Register(connection);
				} catch (Exception) {
					// it happens for some reason, investigating
				}
			}
		}
	}
}
