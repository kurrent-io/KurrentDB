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
	private readonly object _hardenLock = new();
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
	// the one-time setups on it, then hardens and locks the instance configuration.
	private ConnectionPoolWithFunctions CreatePool(bool isReadOnly, bool log,
		IReadOnlyList<IDuckDBSetup> oneTime, IReadOnlyList<string> allowedDirectories) {
		var availableRamMib = CalculateRam();
		var duckDbRamMib = (int)(availableRamMib * 0.25);
		var settings = new Dictionary<string, string> {
			["memory_limit"] = $"{duckDbRamMib}MB", // total, not per connection
			["access_mode"] = isReadOnly ? "READ_ONLY" : "READ_WRITE",
			["temp_directory"] = _tempDirectory,
			// security settings; allowed_directories, external access and the config lock can't be
			// carried in the connection string - DuckDB refuses to set allowed_directories before
			// the database is started - so every pool applies them post-open via HardenInstance
			["allow_community_extensions"] = "false",
		};

		if (_maxTempDirectorySizeBytes > 0L)
			settings["max_temp_directory_size"] = $"{_maxTempDirectorySizeBytes}B";

		var pool = new ConnectionPoolWithFunctions($"Data Source={_path};{GetParamsString()}", _repeated);

		using (pool.Rent(out var connection)) {
			foreach (var s in oneTime)
				s.Execute(connection);

			HardenInstance(connection, allowedDirectories);
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

	// Harden the underlying DuckDB instance once: allow-list, external access off, config lock - in
	// that order (allowed_directories is only settable while external access is on), and only after
	// the setups, which need the unlocked state. Pools with the same connection string share an
	// instance, so a later pool finds it already locked with the same settings and skips;
	// _hardenLock closes the check-then-set race.
	private void HardenInstance(DuckDBAdvancedConnection connection, IReadOnlyList<string> allowedDirectories) {
		lock (_hardenLock) {
			if (IsLocked(connection))
				return;

			var dirs = string.Join(", ", allowedDirectories.Select(d => $"'{d.Replace('\\', '/').Replace("'", "''")}'"));
			connection.ExecuteAdHocNonQuery(
				$"SET allowed_directories=[{dirs}]; SET enable_external_access=false; SET lock_configuration=true;",
				multipleStatements: true);
		}

		return;

		static bool IsLocked(DuckDBAdvancedConnection connection) {
			using var result = connection.ExecuteAdHocQuery("SELECT current_setting('lock_configuration')::VARCHAR"u8);
			while (result.TryFetch(out var chunk))
				using (chunk)
					if (chunk.TryRead(out var row))
						return row.ReadString() == "true";
			return false;
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
