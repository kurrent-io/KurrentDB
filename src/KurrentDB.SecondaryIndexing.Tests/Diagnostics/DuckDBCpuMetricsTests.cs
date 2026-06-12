// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Diagnostics.Metrics;
using KurrentDB.Core.DuckDB;

namespace KurrentDB.SecondaryIndexing.Tests.Diagnostics;

public class DuckDBCpuMetricsTests {
	[Fact]
	public void busy_scope_records_cpu_seconds_with_activity_and_source_tags() {
		using var meter = new Meter("test");
		var metrics = new DuckDBCpuMetrics(meter, "kurrentdb");

		List<(double Value, KeyValuePair<string, object?>[] Tags)> measurements = [];
		using var listener = Listen(meter, measurements);

		using (metrics.Measure(DuckDBCpuMetrics.Activities.Commit)) {
			// busy spin long enough to accrue CPU past Windows' quantum-granular thread accounting
			var stopwatch = Stopwatch.StartNew();
			while (stopwatch.ElapsedMilliseconds < 100) {
			}
		}

		var measurement = Assert.Single(measurements);
		Assert.InRange(measurement.Value, 0.001, 30);
		Assert.Contains(measurement.Tags, t => t is { Key: "activity", Value: "commit" });
		Assert.Contains(measurement.Tags, t => t is { Key: "source", Value: "caller" });
	}

	[Fact]
	public void idle_scope_records_at_most_negligible_cpu() {
		using var meter = new Meter("test");
		var metrics = new DuckDBCpuMetrics(meter, "kurrentdb");

		List<(double Value, KeyValuePair<string, object?>[] Tags)> measurements = [];
		using var listener = Listen(meter, measurements);

		using (metrics.Measure(DuckDBCpuMetrics.Activities.Read)) {
			Thread.Sleep(100);
		}

		Assert.True(measurements.Sum(m => m.Value) < 0.05, $"expected near-zero CPU, got {measurements.Sum(m => m.Value)}s");
	}

	private static MeterListener Listen(Meter meter, List<(double, KeyValuePair<string, object?>[])> measurements) {
		var listener = new MeterListener();
		listener.InstrumentPublished = (instrument, l) => {
			if (instrument.Meter == meter && instrument.Name == "kurrentdb.duckdb.cpu.seconds")
				l.EnableMeasurementEvents(instrument);
		};
		listener.SetMeasurementEventCallback<double>(
			(_, value, tags, _) => measurements.Add((value, tags.ToArray())));
		listener.Start();
		return listener;
	}
}
