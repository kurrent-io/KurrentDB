// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Runtime;
using FluentAssertions;
using KurrentDB.Common.Configuration;
using KurrentDB.Core.Metrics;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Metrics;

public class SystemMetricsTests : IDisposable {
	private readonly TestMeterListener<float> _floatListener;
	private readonly TestMeterListener<double> _doubleListener;
	private readonly TestMeterListener<long> _longListener;
	private readonly FakeClock _clock = new();
	private readonly SystemMetrics _sut;
	private readonly Meter _meter;
	public SystemMetricsTests() {
		_meter = new Meter($"{typeof(ProcessMetricsTests)}");
		_floatListener = new TestMeterListener<float>(_meter);
		_doubleListener = new TestMeterListener<double>(_meter);
		_longListener = new TestMeterListener<long>(_meter);

		var config = new Dictionary<MetricsConfiguration.SystemTracker, bool>();

		foreach (var value in Enum.GetValues<MetricsConfiguration.SystemTracker>()) {
			config[value] = true;
		}
		_sut = new SystemMetrics(_meter, TimeSpan.FromSeconds(42), config, legacyNames: false);
		_sut.CreateLoadAverageMetric("eventstore-sys-load-avg", new() {
			{ MetricsConfiguration.SystemTracker.LoadAverage1m, "1m" },
			{ MetricsConfiguration.SystemTracker.LoadAverage5m, "5m" },
			{ MetricsConfiguration.SystemTracker.LoadAverage15m, "15m" },
		});

		_sut.CreateCpuMetric("eventstore-sys-cpu");

		_sut.CreateMemoryMetric("eventstore-sys-mem", new() {
			{ MetricsConfiguration.SystemTracker.FreeMem, "free" },
			{ MetricsConfiguration.SystemTracker.TotalMem, "total" },
		});

		_sut.CreateDiskMetric("eventstore-sys-disk", ["."], DriveStats.GetDriveInfo, new() {
			{ MetricsConfiguration.SystemTracker.DriveTotalBytes, "total" },
			{ MetricsConfiguration.SystemTracker.DriveUsedBytes, "used" },
		});

		_floatListener.Observe();
		_doubleListener.Observe();
		_longListener.Observe();
	}

	public void Dispose() {
		_floatListener.Dispose();
		_doubleListener.Dispose();
		_longListener.Dispose();
	}

	[Fact]
	public void can_collect_sys_load_avg() {
		if (RuntimeInformation.IsWindows)
			return;

		Assert.Collection(
			_doubleListener.RetrieveMeasurements("eventstore-sys-load-avg"),
			m => {
				Assert.True(m.Value > 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("period", tag.Key);
						Assert.Equal("1m", tag.Value);
					});
			},
			m => {
				Assert.True(m.Value > 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("period", tag.Key);
						Assert.Equal("5m", tag.Value);
					});
			},
			m => {
				Assert.True(m.Value > 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("period", tag.Key);
						Assert.Equal("15m", tag.Value);
					});
			});
	}

	[Fact]
	public void can_collect_sys_cpu() {
		Assert.Collection(
			_doubleListener.RetrieveMeasurements("eventstore-sys-cpu"),
			m => {
				Assert.True(m.Value >= 0);
				Assert.Empty(m.Tags);
			});
	}

	[Fact]
	public void can_collect_sys_cpu_using_metrics_collector() {
		// Arrange
		using var collector = new MetricCollector<double>(
			null, _meter.Name, "eventstore-sys-cpu"
		);

		// Act
		collector.RecordObservableInstruments();

		// Assert
		collector.LastMeasurement.Should().NotBeNull();
		collector.LastMeasurement!.Value.Should().BeGreaterOrEqualTo(0);
	}



	[Fact]
	public void can_collect_sys_mem() {
		Assert.Collection(
			_longListener.RetrieveMeasurements("eventstore-sys-mem-bytes"),
			m => {
				Assert.True(m.Value > 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("kind", tag.Key);
						Assert.Equal("free", tag.Value);
					});
			},
			m => {
				Assert.True(m.Value > 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("kind", tag.Key);
						Assert.Equal("total", tag.Value);
					});
			});
	}

	private static SystemMetrics CreateSystemMetrics(Meter meter) {
		var config = new Dictionary<MetricsConfiguration.SystemTracker, bool>();
		foreach (var value in Enum.GetValues<MetricsConfiguration.SystemTracker>())
			config[value] = true;

		return new SystemMetrics(meter, TimeSpan.FromSeconds(42), config, legacyNames: false);
	}

	[Fact]
	public void deduplicates_disk_paths_on_the_same_drive() {
		// Different paths that resolve to the same drive should only produce a single
		// (kind, disk) series per kind, not one per path.
		using var meter = new Meter($"{typeof(SystemMetricsTests)}.dedupe");
		using var listener = new TestMeterListener<long>(meter);

		DriveData GetDriveInfo(string path) => new("the-only-disk", TotalBytes: 1000, AvailableBytes: 400);

		var sut = CreateSystemMetrics(meter);
		sut.CreateDiskMetric("eventstore-sys-disk", ["/db", "/index", "/logs"], GetDriveInfo, new() {
			{ MetricsConfiguration.SystemTracker.DriveTotalBytes, "total" },
			{ MetricsConfiguration.SystemTracker.DriveUsedBytes, "used" },
		});

		listener.Observe();

		// One "used" and one "total" measurement, despite three paths on the same disk.
		listener.RetrieveMeasurements("eventstore-sys-disk-bytes").Should().HaveCount(2);
	}

	[Fact]
	public void separate_drives_produce_separate_stats() {
		// Paths on different drives should each produce their own (kind, disk) series.
		using var meter = new Meter($"{typeof(SystemMetricsTests)}.twodisks");
		using var listener = new TestMeterListener<long>(meter);

		DriveData GetDriveInfo(string path) => path switch {
			"/db" => new DriveData("disk-a", TotalBytes: 1000, AvailableBytes: 400), // used 600
			"/logs" => new DriveData("disk-b", TotalBytes: 5000, AvailableBytes: 1000), // used 4000
			_ => throw new ArgumentOutOfRangeException(nameof(path), path, null),
		};

		var sut = CreateSystemMetrics(meter);
		sut.CreateDiskMetric("eventstore-sys-disk", ["/db", "/logs"], GetDriveInfo, new() {
			{ MetricsConfiguration.SystemTracker.DriveTotalBytes, "total" },
			{ MetricsConfiguration.SystemTracker.DriveUsedBytes, "used" },
		});

		listener.Observe();

		var measurements = listener.RetrieveMeasurements("eventstore-sys-disk-bytes");

		// used + total for each of the two distinct disks.
		measurements.Should().HaveCount(4);

		long ValueFor(string disk, string kind) => measurements
			.Single(m => m.Tags.Any(t => t.Key == "disk" && (string)t.Value == disk) &&
						 m.Tags.Any(t => t.Key == "kind" && (string)t.Value == kind))
			.Value;

		ValueFor("disk-a", "total").Should().Be(1000);
		ValueFor("disk-a", "used").Should().Be(600);
		ValueFor("disk-b", "total").Should().Be(5000);
		ValueFor("disk-b", "used").Should().Be(4000);
	}

	[Fact]
	public void can_collect_sys_disk() {
		Assert.Collection(
			_longListener.RetrieveMeasurements("eventstore-sys-disk-bytes"),
			m => {
				Assert.True(m.Value >= 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("kind", tag.Key);
						Assert.Equal("used", tag.Value);
					},
					tag => {
						Assert.Equal("disk", tag.Key);
						Assert.NotNull(tag.Value);
					});
			},
			m => {
				Assert.True(m.Value >= 0);
				Assert.Collection(
					m.Tags,
					tag => {
						Assert.Equal("kind", tag.Key);
						Assert.Equal("total", tag.Value);
					},
					tag => {
						Assert.Equal("disk", tag.Key);
						Assert.NotNull(tag.Value);
					});
			});
	}
}
