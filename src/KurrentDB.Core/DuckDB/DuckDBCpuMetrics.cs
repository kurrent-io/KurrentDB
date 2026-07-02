// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Kurrent.Quack;

namespace KurrentDB.Core.DuckDB;

// Reports the cumulative CPU time consumed by the DuckDB executor's worker and dispatcher threads as an
// observable counter, tagged by role. This is the whole point of routing DuckDB work through the executor:
// all DuckDB CPU is now attributable to threads the executor owns, so a single SampleCpu() reading sums them.
public class DuckDBCpuMetrics {
	public const string MeterName = "KurrentDB.DuckDB";

	public DuckDBCpuMetrics(Meter meter, string serviceName, Func<IReadOnlyList<DuckDBExecutor.CpuSample>> sampleCpu) {
		meter.CreateObservableCounter($"{serviceName}.duckdb.cpu.seconds", Observe,
			description: "CPU time consumed by DuckDB's executor worker and dispatcher threads, in seconds " +
				"(excludes DuckDB work SchemaRegistry runs on its own threads via a shared connection pool; " +
				"that work's parallel portions still run on the executor's worker threads).");

		IEnumerable<Measurement<double>> Observe() {
			double workers = 0, dispatchers = 0;
			foreach (var s in sampleCpu())
				if (s.Role == "worker") workers += s.CpuSeconds; else dispatchers += s.CpuSeconds;
			yield return new(workers, new KeyValuePair<string, object?>("role", "worker"));
			yield return new(dispatchers, new KeyValuePair<string, object?>("role", "dispatcher"));
		}
	}
}
