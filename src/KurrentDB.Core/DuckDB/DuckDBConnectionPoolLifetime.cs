// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using DotNext;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.DuckDB;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Core.DuckDB;

public class DuckDBConnectionPoolLifetime : Disposable {
	private readonly string _path;
	private readonly IReadOnlyList<IDuckDBSetup> _repeated;
	private readonly ILogger<DuckDBConnectionPoolLifetime> _log;
	[CanBeNull] private string _tempPath;

	public DuckDBConnectionPool Shared { get; }

	public DuckDBConnectionPoolLifetime(TFChunkDbConfig config, IEnumerable<IDuckDBSetup> setups, [CanBeNull] ILogger<DuckDBConnectionPoolLifetime> log) {
		_path = config.InMemDb ? GetTempPath() : $"{config.Path}/kurrent.ddb";
		_log = log;

		var once = new List<IDuckDBSetup>();
		var repeated = new List<IDuckDBSetup>();
		foreach (var duckDBSetup in setups) {
			if (duckDBSetup.OneTimeOnly) {
				once.Add(duckDBSetup);
			} else {
				repeated.Add(duckDBSetup);
			}
		}
		_repeated = repeated;

		Shared = CreatePool(isReadOnly: false); // Writes can be done to DuckDB only with the shared pool
		using var connection = Shared.Open();
		foreach (var s in once)
			s.Execute(connection);

		return;

		string GetTempPath() {
			_tempPath = Path.GetTempFileName();
			File.Delete(_tempPath);
			return _tempPath;
		}
	}

	public DuckDBConnectionPool CreatePool() => CreatePool(isReadOnly: true);

	private DuckDBConnectionPool CreatePool(bool isReadOnly) {
		var accessMode = isReadOnly ? "READ_ONLY" : "READ_WRITE";
		var pool = new ConnectionPoolWithFunctions($"Data Source={_path};access_mode={accessMode};", _repeated);
		_log?.LogTrace("Created {type} DuckDB connection pool at {path}", accessMode, _path);
		return pool;
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			_log?.LogDebug("Checkpointing DuckDB connection");
			var connection = Shared.Open();
			connection.Checkpoint();
			connection.Dispose();
			Shared.Dispose();
			if (_tempPath != null) {
				try {
					File.Delete(_tempPath);
				} catch (IOException) {
					// let the file stay and be cleaned up by the OS
				}
			}
			_log?.LogInformation("Disposed DuckDB connection pool");
		}

		base.Dispose(disposing);
	}

	private class ConnectionPoolWithFunctions(string connectionString, IReadOnlyList<IDuckDBSetup> setup) : DuckDBConnectionPool(connectionString) {
		[Experimental("DuckDBNET001")]
		protected override void Initialize(DuckDBAdvancedConnection connection) {
			base.Initialize(connection);
			for (var i = 0; i < setup.Count; i++) {
				try {
					setup[i].Execute(connection);
				} catch (Exception) {
					// it happens for some reason, investigating
				}
			}
		}
	}
}
