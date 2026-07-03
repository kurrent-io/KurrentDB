// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.DuckDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KurrentDB.Core.DuckDB;

public static class InjectionExtensions {
	public static IServiceCollection AddDuckDb(this IServiceCollection services, string serviceName, int workerCount, int dispatcherCount) {
		services.AddSingleton(sp => new DuckDBExecutorLifetime(
			sp.GetRequiredService<TFChunkDbConfig>(),
			sp.GetServices<IDuckDBSetup>(),
			workerCount,
			dispatcherCount,
			serviceName,
			sp.GetService<ILogger<DuckDBExecutorLifetime>>()));
		services.AddHostedService(sp => sp.GetRequiredService<DuckDBExecutorLifetime>());
		services.AddSingleton<DuckDBExecutor>(sp => sp.GetRequiredService<DuckDBExecutorLifetime>().Executor);
		// SchemaRegistry reaches DuckDB through Kurrent.Surge.DuckDB's IDuckDBConnectionProvider, which is
		// constructed from a DuckDBConnectionPool. Hand it a pool that SHARES the executor's already-open database
		// (same file => same in-process DuckDB instance and task scheduler), so its query work still runs on the
		// executor's measured worker threads. This is a bridge for that external framework's hard requirement, not a
		// revival of the removed shared-RW / per-connection READ_ONLY pool model. DI owns and disposes this pool;
		// its lifetime is independent of the executor's own internal pool.
		services.AddSingleton<DuckDBConnectionPool>(sp =>
			sp.GetRequiredService<DuckDBExecutorLifetime>().Executor.CreateSharedConnectionPool());
		// The lifetime is a hosted service, so it's instantiated at node startup; it creates the CPU-metric
		// instrument in its constructor (over the executor it owns), so the instrument exists even without a
		// metrics consumer attached.
		services.AddSingleton(sp => sp.GetRequiredService<DuckDBExecutorLifetime>().CpuMetrics);
		return services;
	}
}
