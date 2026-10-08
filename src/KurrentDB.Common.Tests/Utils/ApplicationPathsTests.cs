// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.IO;
using KurrentDB.Common.Utils;

namespace KurrentDB.Common.Tests.Utils;

public class ApplicationPathsTests {
	[Fact]
	public void application_directory_is_the_host_base_directory() =>
		Assert.Equal(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), Locations.ApplicationDirectory);

	[Fact]
	public void default_logs_are_beside_the_host() =>
		Assert.Equal(Path.Combine(AppContext.BaseDirectory, "es-logs"), Helper.GetDefaultLogsDir());

	[Fact]
	public void version_prefix_belongs_to_the_database_not_its_host() {
		var version = typeof(VersionInfo).Assembly.GetName().Version!.ToString();
		if (version.EndsWith(".0"))
			version = version[..^2];
		Assert.Equal(version, VersionInfo.VersionPrefix);
	}
}
