// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using Kurrent.Quack;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.DuckDB;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KurrentDB.Core.DuckDB;

// Owns the DuckDB executor: database open (with thread + memory settings applied at open),
// one-time schema setups, the shutdown checkpoint, and executor disposal.
public sealed class DuckDBExecutorLifetime : Disposable, IHostedService {
	private readonly ILogger<DuckDBExecutorLifetime> _log;
	[CanBeNull] private string _tempPath;

	public DuckDBExecutor Executor { get; }

	// The CPU metric is created here (eagerly, at node startup) rather than via an activated DI singleton so the
	// instrument exists even without a metrics consumer, and so it samples the very executor this lifetime owns.
	public DuckDBCpuMetrics CpuMetrics { get; }

	public DuckDBExecutorLifetime(
		TFChunkDbConfig config,
		IEnumerable<IDuckDBSetup> setups,
		int workerCount,
		int dispatcherCount,
		string serviceName,
		[CanBeNull] ILogger<DuckDBExecutorLifetime> log) {
		_log = log ?? NullLogger<DuckDBExecutorLifetime>.Instance;

		var path = config.InMemDb ? GetTempPath() : $"{config.Path}/kurrent.ddb";
		var memoryMib = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024 * 0.25); // 25% of RAM, as before
		var connectionString = $"Data Source={path};memory_limit={memoryMib}MB";

		Executor = new DuckDBExecutor(connectionString, workerCount, dispatcherCount);
		try {
			CpuMetrics = new DuckDBCpuMetrics(new Meter(DuckDBCpuMetrics.MeterName, "1.0.0"), serviceName, Executor.SampleCpu);
			_log.LogInformation("DuckDB executor started at {path}: {workers} workers, {dispatchers} dispatchers, memory_limit {memory}MB",
				path, workerCount, dispatcherCount, memoryMib);

			// One-time setups (IndexingDbSchema, SchemaDbSchema) — effects are database-wide, so running
			// them once on any executor-owned connection preserves today's semantics exactly.
			Executor.Execute(connection => {
				foreach (var setup in setups)
					setup.Execute(connection);
				return 0;
			}, CancellationToken.None).AsTask().GetAwaiter().GetResult();
		} catch {
			// A setup failure (e.g. a schema-migration error) throws before this object exists for anyone to
			// Dispose(), so the executor's worker/dispatcher threads and open DB handle would leak. Dispose it.
			Executor.DisposeAsync().AsTask().GetAwaiter().GetResult();
			throw;
		}

		return;

		string GetTempPath() {
			_tempPath = Path.GetTempFileName();
			File.Delete(_tempPath); // DuckDB refuses a pre-existing empty file; recreate at the same path
			return _tempPath;
		}
	}

	public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public async Task StopAsync(CancellationToken cancellationToken) {
		_log.LogDebug("Checkpointing DuckDB");
		await Executor.Execute(connection => {
			connection.Checkpoint();
			return 0;
		}, cancellationToken);
	}

	protected override void Dispose(bool disposing) {
		if (disposing) {
			Executor.DisposeAsync().AsTask().GetAwaiter().GetResult();
			if (_tempPath != null) {
				try {
					File.Delete(_tempPath);
				} catch (IOException) {
					// let the OS clean it up
				}
			}
		}
		base.Dispose(disposing);
	}
}
