// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using Kurrent.Quack;
using KurrentDB.Core.DuckDB;

namespace KurrentDB.SecondaryIndexing.Tests.Diagnostics;

public class DuckDBCpuMetricsTests {
	[Fact]
	public void observable_counter_reports_per_role_cpu_sums() {
		using var meter = new Meter("test");
		var samples = new List<DuckDBExecutor.CpuSample> {
			new("worker", 1.5), new("worker", 0.5), new("dispatcher", 0.25),
		};
		_ = new DuckDBCpuMetrics(meter, "kurrentdb", () => samples);

		List<(double Value, string Role)> observed = [];
		using var listener = new MeterListener();
		listener.InstrumentPublished = (i, l) => {
			if (i.Meter == meter && i.Name == "kurrentdb.duckdb.cpu.seconds") l.EnableMeasurementEvents(i);
		};
		listener.SetMeasurementEventCallback<double>((_, value, tags, _) => {
			foreach (var t in tags) if (t.Key == "role") observed.Add((value, (string)t.Value!));
		});
		listener.Start();
		listener.RecordObservableInstruments();

		Assert.Contains(observed, m => m is { Role: "worker", Value: 2.0 });
		Assert.Contains(observed, m => m is { Role: "dispatcher", Value: 0.25 });
	}
}
