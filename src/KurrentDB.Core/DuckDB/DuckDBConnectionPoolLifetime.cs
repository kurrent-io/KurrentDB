// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
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
	private readonly string _logsDir;
	private readonly string _tempDirectory;
	private readonly long _maxTempDirectorySizeBytes;
	private readonly IReadOnlyList<IDuckDBSetup> _repeated;
	private readonly ILogger<DuckDBConnectionPoolLifetime> _log;
	[CanBeNull] private string _tempPath;

	public DuckDBConnectionPool Shared { get; }

	public DuckDBConnectionPoolLifetime(
		TFChunkDbConfig config,
		ClusterVNodeOptions nodeOptions,
		IEnumerable<IDuckDBSetup> setups,
		[CanBeNull] ILogger<DuckDBConnectionPoolLifetime> log) {

		_path = config.InMemDb ? GetTempPath() : $"{config.Path}/kurrent.ddb";
		_logsDir = nodeOptions.GetLogsDirectory();
		_tempDirectory = Path.GetFullPath(config.SqlEngineTempDirectory is { Length: > 0 } tempPath
			? tempPath
			: $"{_path}.tmp"); // the same directory DuckDB would pick by default. explicit so we can clean it up

		_maxTempDirectorySizeBytes = config.SqlEngineTempDirectorySizeLimit;
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

		Shared = CreatePool(isReadOnly: false, log: true, oneTime: once, allowedDirectories: [_logsDir]);

		return;

		string GetTempPath() {
			_tempPath = Path.GetTempFileName();
			File.Delete(_tempPath);
			return _tempPath;
		}
	}

	public DuckDBConnectionPool CreatePool() =>
		CreatePool(isReadOnly: true, log: false, oneTime: [], allowedDirectories: []); // no writes go through here so set read only

	// The only way to obtain a pool - never returns one unlocked. Opens the first connection, runs
	// the one-time setups on it, then locks the instance configuration.
	private ConnectionPoolWithFunctions CreatePool(bool isReadOnly, bool log,
		IReadOnlyList<IDuckDBSetup> oneTime, IReadOnlyList<string> allowedDirectories) {
		var availableRamMib = CalculateRam();
		var duckDbRamMib = (int)(availableRamMib * 0.25);
		var settings = new Dictionary<string, string> {
			["memory_limit"] = $"{duckDbRamMib}MB", // total, not per connection
			["access_mode"] = isReadOnly ? "READ_ONLY" : "READ_WRITE",
			["temp_directory"] = _tempDirectory,
			// security settings; the rest (allowed_directories, external access, config lock) are
			// order-dependent, so every pool applies them post-open on the first connection below
			["allow_community_extensions"] = "false",
		};

		if (_maxTempDirectorySizeBytes > 0L)
			settings["max_temp_directory_size"] = $"{_maxTempDirectorySizeBytes}B";

		var pool = new ConnectionPoolWithFunctions($"Data Source={_path};{GetParamsString()}", _repeated);

		using (pool.Rent(out var connection)) {
			foreach (var s in oneTime)
				s.Execute(connection);

			// Restrict the instance's file access to the allowed directories (the node's own log
			// directory for the Shared pool, nothing for read-only pools), then lock it down. Order
			// matters and can't be expressed in the connection string: allowed_directories must be
			// set while external access is still on, then external access is disabled, then the
			// config is locked. These are global settings, so applying them once on the first
			// connection covers the whole pool. The lock comes after the one-time setups because
			// function registration needs the unlocked state.
			var dirs = string.Join(", ", allowedDirectories.Select(d => $"'{d.Replace('\\', '/').Replace("'", "''")}'"));
			connection.ExecuteAdHocNonQuery(
				$"SET allowed_directories=[{dirs}]; SET enable_external_access=false; SET lock_configuration=true;",
				multipleStatements: true);
		}

		if (log)
			_log.LogInformation("Created DuckDB connection pool at {path} with {settings}", _path, settings);
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

	public Task StartAsync(CancellationToken cancellationToken) {
		var task = Task.CompletedTask;
		try {
			var tempDir = new DirectoryInfo(_tempDirectory);
			// cleanup tmp files on startup
			if (tempDir.Exists) {
				DeleteTempObjects(tempDir);
			}
		} catch (Exception e) {
			task = Task.FromException(e);
		}

		return task;

		static void DeleteTempObjects(DirectoryInfo tempDir) {
			foreach (var tempObj in tempDir.EnumerateFileSystemInfos("*.tmp", SearchOption.TopDirectoryOnly)) {
				if (tempObj is DirectoryInfo subDir) {
					subDir.Delete(recursive: true);
				} else {
					tempObj.Delete();
				}
			}
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) {
		_log.LogDebug("Checkpointing DuckDB connection");
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
			Shared.Dispose();
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
