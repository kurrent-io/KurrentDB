// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace KurrentDB.Core.Metrics;

public class InternalExporter : BaseExporter<Metric>, IPullMetricExporter {
	public readonly MetersSnapshot Snapshot = new();

	public override ExportResult Export(in Batch<Metric> batch) {
		if (DateTime.Now.Subtract(_lastCollected).TotalMilliseconds < 1000)
			return ExportResult.Success;

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
					Snapshot.Disks = ReadDiskMetric(metric);
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
					if (te.Current.Key != "activity")
						continue;
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
	}

	// Each drive contributes one point per kind, so pair the kinds back up by disk name before
	// reading their values. Taken independently, a "used" point is indistinguishable from its
	// drive's total.
	static IReadOnlyList<DiskUsage> ReadDiskMetric(Metric metric) {
		var readings = new List<(string Disk, string Kind, long Value)>();

		var points = metric.GetMetricPoints().GetEnumerator();
		while (points.MoveNext()) {
			var point = points.Current;

			string disk = null;
			string kind = null;
			var tags = point.Tags.GetEnumerator();
			while (tags.MoveNext()) {
				switch (tags.Current.Key) {
					case "disk":
						disk = (string)tags.Current.Value;
						break;
					case "kind":
						kind = (string)tags.Current.Value;
						break;
				}
			}

			if (disk is not null && kind is not null)
				readings.Add((disk, kind, point.GetGaugeLastValueLong()));
		}

		return DiskUsage.Combine(readings);
	}

	void HandleCheckpoint(Metric metric) {
		var enumerator = metric.GetMetricPoints().GetEnumerator();
		while (enumerator.MoveNext()) {
			var current = enumerator.Current;
			var te = current.Tags.GetEnumerator();
			te.MoveNext();
			if (te.Current.Key != "name" || (string)te.Current.Value != "writer")
				continue;

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

		// Replaced wholesale on each export rather than mutated in place: the UI reads this on the
		// render thread while the exporter writes it on the collection thread.
		public volatile IReadOnlyList<DiskUsage> Disks = [];
	}
}

/// <summary>
/// Total and used space for one drive, as reported by the sys-disk metric.
/// </summary>
public readonly record struct DiskUsage(string Name, long TotalBytes, long UsedBytes) {
	public double UsagePercent => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100 : 0;

	/// <summary>
	/// Pairs the "total" and "used" readings of each disk back together, discards disks whose stats
	/// could not be read, and orders what is left by disk name.
	/// </summary>
	public static IReadOnlyList<DiskUsage> Combine(IEnumerable<(string Disk, string Kind, long Value)> readings) {
		var byDisk = new Dictionary<string, DiskUsage>();
		foreach (var (disk, kind, value) in readings) {
			byDisk.TryGetValue(disk, out var usage);
			usage = usage with { Name = disk };
			byDisk[disk] = kind switch {
				"total" => usage with { TotalBytes = value },
				"used" => usage with { UsedBytes = value },
				_ => usage,
			};
		}

		// A drive whose stats could not be read reports a zero total (DriveStats falls back to
		// "Unknown", 0, 0). There is nothing to show for it, and it would divide by zero.
		var disks = new List<DiskUsage>(byDisk.Count);
		foreach (var usage in byDisk.Values)
			if (usage.TotalBytes > 0)
				disks.Add(usage);

		// Dictionary order is not stable between exports; sort so the UI's cards keep their place.
		disks.Sort(static (x, y) => string.Compare(x.Name, y.Name, StringComparison.Ordinal));
		return disks;
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
