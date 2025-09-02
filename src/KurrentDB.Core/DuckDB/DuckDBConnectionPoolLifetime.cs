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
using Microsoft.Extensions.Logging;

namespace KurrentDB.Core.DuckDB;

public class DuckDBConnectionPoolLifetime : Disposable {
	private readonly DuckDBConnectionPool _pool;
	private readonly ILogger<DuckDBConnectionPoolLifetime> _log;

	public DuckDBConnectionPoolLifetime(TFChunkDbConfig config, IEnumerable<IDuckDBInlineFunction> inlineFunctions, [CanBeNull] ILogger<DuckDBConnectionPoolLifetime> log) {
		var path = config.InMemDb ? ":memory:" : $"{config.Path}/kurrent.ddb";
		_pool = new ConnectionPoolWithFunctions($"Data Source={path}", inlineFunctions.ToArray());
		log?.LogInformation("Created DuckDB connection pool at {path}", path);
		_log = log;
	}

	public DuckDBConnectionPool GetConnectionPool() => _pool;

	protected override void Dispose(bool disposing) {
		if (disposing) {
			_log?.LogDebug("Checkpointing DuckDB connection");
			using (var connection = _pool.Open()) {
				connection.Checkpoint();
			}

			_pool.Dispose();
			_log?.LogInformation("Disposed DuckDB connection pool");
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
