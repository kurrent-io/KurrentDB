// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace KurrentDB.Core.DuckDB;

// Attributes CPU consumed by DuckDB operations executed on KurrentDB threads.
// DuckDB also schedules work onto its own native worker threads; that share is not visible
// to caller-side measurement and is reported separately once KurrentDB owns those workers.
public class DuckDBCpuMetrics {
	public const string MeterName = "KurrentDB.DuckDB";

	public static class Activities {
		public const string Query = "query";
		public const string Read = "read";
		public const string Commit = "commit";
		public const string Checkpoint = "checkpoint";
	}

	private static readonly KeyValuePair<string, object> SourceTag = new("source", "caller");

	private readonly Counter<double> _cpuSeconds;

	// Reads the calling thread's cumulative CPU time in nanoseconds. Null when per-thread CPU
	// time is unavailable on this platform, in which case measurement is a no-op. Injectable so
	// tests can supply a deterministic source instead of relying on real per-thread CPU accounting.
	private readonly Func<long> _currentCpuNanoseconds;

	public DuckDBCpuMetrics(Meter meter, string serviceName, Func<long> currentCpuNanoseconds = null) {
		_cpuSeconds = meter.CreateCounter<double>(
			$"{serviceName}.duckdb.cpu.seconds",
			description: "CPU time consumed by DuckDB operations on KurrentDB threads, in seconds");
		_currentCpuNanoseconds = currentCpuNanoseconds
			?? (ThreadCpuTime.IsSupported ? () => ThreadCpuTime.CurrentNanoseconds : (Func<long>)null);
	}

	public CpuScope Measure(string activity) => new(this, activity);

	// A ref struct so it cannot live across an await: the CPU delta is only valid when start
	// and stop are read on the same thread.
	public readonly ref struct CpuScope {
		private readonly DuckDBCpuMetrics _metrics;
		private readonly string _activity;
		private readonly long _startNanoseconds;

		internal CpuScope(DuckDBCpuMetrics metrics, string activity) {
			if (metrics._currentCpuNanoseconds is null)
				return; // per-thread CPU time unavailable on this platform: no-op

			_metrics = metrics;
			_activity = activity;
			_startNanoseconds = metrics._currentCpuNanoseconds();
		}

		public void Dispose() {
			if (_metrics is null)
				return;

			var elapsedNanoseconds = _metrics._currentCpuNanoseconds!() - _startNanoseconds;
			if (elapsedNanoseconds > 0)
				_metrics._cpuSeconds.Add(
					elapsedNanoseconds / 1e9,
					new KeyValuePair<string, object>("activity", _activity),
					SourceTag);
		}
	}
}
