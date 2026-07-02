// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;

namespace KurrentDB.Core.DuckDB;

// Shell for now — Task 5b wires the instrument that reports DuckDBExecutor.SampleCpu() readings.
public class DuckDBCpuMetrics {
	public const string MeterName = "KurrentDB.DuckDB";

	public DuckDBCpuMetrics(Meter meter, string serviceName) {
	}
}
