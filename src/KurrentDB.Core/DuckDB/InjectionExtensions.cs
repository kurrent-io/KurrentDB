// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Kurrent.Quack;
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
			sp.GetService<ILogger<DuckDBExecutorLifetime>>()));
		services.AddHostedService(sp => sp.GetRequiredService<DuckDBExecutorLifetime>());
		services.AddSingleton<DuckDBExecutor>(sp => sp.GetRequiredService<DuckDBExecutorLifetime>().Executor);
		services.AddSingleton(new DuckDBCpuMetrics(new Meter(DuckDBCpuMetrics.MeterName, "1.0.0"), serviceName)); // Task 5b wires the instrument
		return services;
	}
}
