// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using FluentAssertions;
using KurrentDB.Core.Metrics;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.Metrics;

public class DiskUsageTests {
	[Fact]
	public void pairs_total_and_used_of_each_disk() {
		// "used" is listed before "total" for both disks: read independently of its kind, a used
		// value is indistinguishable from its drive's total and silently replaces it.
		var disks = DiskUsage.Combine([
			("disk-a", "used", 600),
			("disk-a", "total", 1000),
			("disk-b", "used", 4000),
			("disk-b", "total", 5000),
		]);

		disks.Should().Equal(
			new DiskUsage("disk-a", TotalBytes: 1000, UsedBytes: 600),
			new DiskUsage("disk-b", TotalBytes: 5000, UsedBytes: 4000));

		disks[0].UsagePercent.Should().Be(60);
		disks[1].UsagePercent.Should().Be(80);
	}

	[Fact]
	public void orders_by_disk_name() {
		// Readings arrive in whatever order the exporter enumerates them, which is not stable between
		// collections; the UI renders a card per disk and they must not swap places underneath it.
		var disks = DiskUsage.Combine([
			("/mnt/logs", "total", 10),
			("/mnt/logs", "used", 1),
			("/", "total", 10),
			("/", "used", 2),
			("/mnt/index", "total", 10),
			("/mnt/index", "used", 3),
		]);

		disks.Select(x => x.Name).Should().Equal("/", "/mnt/index", "/mnt/logs");
	}

	[Fact]
	public void drops_disks_that_could_not_be_read() {
		// DriveStats falls back to ("Unknown", 0, 0) when it cannot stat a path. There is nothing to
		// report for such a drive, and a zero total would divide by zero in the UI.
		var disks = DiskUsage.Combine([
			("Unknown", "total", 0),
			("Unknown", "used", 0),
			("/", "total", 1000),
			("/", "used", 250),
		]);

		disks.Should().Equal(new DiskUsage("/", TotalBytes: 1000, UsedBytes: 250));
	}

	[Fact]
	public void reports_nothing_when_there_are_no_readings() {
		DiskUsage.Combine([]).Should().BeEmpty();
	}
}
