// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.DuckDB;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Core.DuckDB;

// Manages the lifetime of the Shared pool
// Also produces additional pools on demand that the caller should dispose.
public class DuckDBConnectionPoolLifetime : Disposable, IHostedService {
	private readonly string _path;
	private readonly IReadOnlyList<IDuckDBSetup> _repeated;
	private readonly ILogger<DuckDBConnectionPoolLifetime> _log;
	[CanBeNull] private string _tempPath;

	public DuckDBConnectionPool Shared { get; }

	public DuckDBConnectionPoolLifetime(
		TFChunkDbConfig config,
		IEnumerable<IDuckDBSetup> setups,
		[CanBeNull] ILogger<DuckDBConnectionPoolLifetime> log) {

		_path = config.InMemDb ? GetTempPath() : $"{config.Path}/kurrent.ddb";
		_log = log ?? NullLogger<DuckDBConnectionPoolLifetime>.Instance;

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

		Shared = CreatePool(isReadOnly: false, log: true);
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

	public DuckDBConnectionPool CreatePool() => CreatePool(isReadOnly: true, log: false); // no writes go through here so set read only

	private DuckDBConnectionPool CreatePool(bool isReadOnly, bool log) {
		var availableRamMib = CalculateRam();
		var duckDbRamMib = (int)(availableRamMib * 0.25);
		var settings = new Dictionary<string, string> {
			["memory_limit"] = $"{duckDbRamMib}MB",
			["access_mode"] = isReadOnly ? "READ_ONLY" : "READ_WRITE",
		};
		var pool = new ConnectionPoolWithFunctions($"Data Source={_path};{GetParamsString()}", _repeated);
		if (log)
			_log?.LogInformation("Created DuckDB connection pool at {path} with {settings}", _path, settings);
		return pool;

		static long CalculateRam() {
			var totalRam = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
			return totalRam / 1024 / 1024;
		}

		string GetParamsString() {
			var list = settings.Keys.Select(x => $"{x}={settings[x]}");
			return string.Join(";", list);
		}
	}

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StopAsync(CancellationToken cancellationToken) {
		_log?.LogDebug("Checkpointing DuckDB connection");
		var connection = Shared.Open();
		try {
			connection.Checkpoint();
		} catch (Exception ex) {
			return Task.FromException(ex);
		} finally {
			connection.Dispose();
		}

		return Task.CompletedTask;
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			if (_tempPath != null) {
				try {
					File.Delete(_tempPath);
				} catch (IOException) {
					// let the file stay and be cleaned up by the OS
				}
			}
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
