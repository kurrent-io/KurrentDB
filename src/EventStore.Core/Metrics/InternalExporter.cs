// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace EventStore.Core.Metrics;

public class InternalExporter : BaseExporter<Metric>, IPullMetricExporter {
	public readonly MetersSnapshot Snapshot = new();

	public override ExportResult Export(in Batch<Metric> batch) {
		if (DateTime.Now.Subtract(_lastCollected).TotalMilliseconds < 1000) return ExportResult.Success;

		_lastCollected = DateTime.Now;
		foreach (var metric in batch) {
			switch (metric.Name) {
				case "kurrentdb-checkpoints":
					HandleCheckpoint(metric);
					continue;
				case "kurrentdb-io-events":
					ProcessEventsMetric(metric, ref Snapshot.EventsRead, ref Snapshot.EventsWritten);
					break;
				case "kurrentdb-io-bytes":
					ProcessEventsMetric(metric, ref Snapshot.EventBytesRead, ref Snapshot.EventBytesWritten);
					break;
				case "kurrentdb-sys-disk":
					ProcessDiskMetric(metric, ref Snapshot.DiskTotalBytes, ref Snapshot.DiskUsedBytes);
					break;
			}
		}

		return ExportResult.Success;

		void ProcessEventsMetric(Metric metric, ref long val1, ref long val2) {
			var enumerator = metric.GetMetricPoints().GetEnumerator();
			while (enumerator.MoveNext()) {
				var current = enumerator.Current;
				var te = current.Tags.GetEnumerator();
				while (te.MoveNext()) {
					if (te.Current.Key != "activity") continue;
					switch ((string)te.Current.Value) {
						case "read":
							val1 = current.GetSumLong();
							break;
						case "written":
							val2 = current.GetSumLong();
							break;
					}
				}
			}
		}

		void ProcessDiskMetric(Metric metric, ref long val1, ref long val2) {
			var enumerator = metric.GetMetricPoints().GetEnumerator();
			while (enumerator.MoveNext()) {
				var current = enumerator.Current;
				var te = current.Tags.GetEnumerator();
				while (te.MoveNext()) {
					switch (te.Current.Key) {
						case "disk":
							val1 = current.GetGaugeLastValueLong();
							break;
						case "kind":
							if ((string)te.Current.Value == "used")
								val2 = current.GetGaugeLastValueLong();
							break;
					}
				}
			}
		}
	}

	void HandleCheckpoint(Metric metric) {
		var enumerator = metric.GetMetricPoints().GetEnumerator();
		while (enumerator.MoveNext()) {
			var current = enumerator.Current;
			var te = current.Tags.GetEnumerator();
			te.MoveNext();
			if (te.Current.Key != "name" || (string)te.Current.Value != "writer") continue;

			// Calculate the writer checkpoint delta and return
			var sum = current.GetSumLong();
			var delta = sum - _last;
			_last = sum;
			Snapshot.EventBytesWritten = delta;
			return;
		}
	}

	long _last;
	DateTime _lastCollected = DateTime.MinValue;

	public Func<int, bool> Collect { get; set; }

	public class MetersSnapshot {
		public long EventsRead;
		public long EventsWritten;
		public long EventBytesRead;
		public long EventBytesWritten;
		public long DiskTotalBytes;
		public long DiskUsedBytes;
	}
}

public static class InternalExporterMeterProviderBuilderExtensions {
	public static MeterProviderBuilder AddInternalExporter(this MeterProviderBuilder builder) {
		builder.ConfigureServices(services => services.AddSingleton<InternalExporter>());
		return builder.AddReader(sp => {
			var exporter = sp.GetRequiredService<InternalExporter>();
			return new BaseExportingMetricReader(exporter) {
				TemporalityPreference = MetricReaderTemporalityPreference.Delta
			};
		});
	}
}
