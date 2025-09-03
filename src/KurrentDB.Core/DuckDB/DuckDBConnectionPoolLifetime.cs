// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using DotNext;
using Google.Protobuf.Compiler;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.DuckDB;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Core.DuckDB;

public class DuckDBConnectionPoolLifetime : Disposable {
	private readonly DuckDBConnectionPool _pool;
	private readonly DuckDBAdvancedConnection _connection;
	private readonly ILogger<DuckDBConnectionPoolLifetime> _log;
	[CanBeNull] private string _tempPath;

	public DuckDBConnectionPoolLifetime(TFChunkDbConfig config, IEnumerable<IDuckDBSetup> setups, [CanBeNull] ILogger<DuckDBConnectionPoolLifetime> log) {
		var path = config.InMemDb ? GetTempPath() : $"{config.Path}/kurrent.ddb";
		var repeated = new List<IDuckDBSetup>();
		var once = new List<IDuckDBSetup>();
		foreach (var duckDBSetup in setups) {
			if (duckDBSetup.OneTimeOnly) {
				once.Add(duckDBSetup);
			} else {
				repeated.Add(duckDBSetup);
			}
		}
		_pool = new ConnectionPoolWithFunctions($"Data Source={path}", repeated.ToArray());
		log?.LogInformation("Created DuckDB connection pool at {path}", path);
		_log = log;
		_connection = _pool.Open();
		foreach (var s in once) {
			s.Execute(_connection);
		}

		return;

		string GetTempPath() {
			_tempPath = Path.GetTempFileName();
			File.Delete(_tempPath);
			return _tempPath;
		}
	}

	public DuckDBConnectionPool GetConnectionPool() => _pool;

	protected override void Dispose(bool disposing) {
		if (disposing) {
			_log?.LogDebug("Checkpointing DuckDB connection");
			_connection.Checkpoint();
			_connection.Dispose();
			_pool.Dispose();
			if (_tempPath != null) File.Delete(_tempPath);
			_log?.LogInformation("Disposed DuckDB connection pool");
		}

		base.Dispose(disposing);
	}

	private class ConnectionPoolWithFunctions(string connectionString, IDuckDBSetup[] setup) : DuckDBConnectionPool(connectionString) {
		[Experimental("DuckDBNET001")]
		protected override void Initialize(DuckDBAdvancedConnection connection) {
			base.Initialize(connection);
			for (var i = 0; i < setup.Length; i++) {
				try {
					setup[i].Execute(connection);
				} catch (Exception) {
					// it happens for some reason, investigating
				}
			}
		}
	}
}
