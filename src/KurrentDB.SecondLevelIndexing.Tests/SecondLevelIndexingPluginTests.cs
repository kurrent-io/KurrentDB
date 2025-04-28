// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Plugins.TestHelpers;
using Microsoft.Extensions.Configuration;

namespace KurrentDB.SecondLevelIndexing.Tests;

public class SecondLevelIndexingPluginTests {
	[Fact]
	public void is_disabled_by_default() {
		using var sut = new SecondLevelIndexingPlugin();

		// when
		TestPluginStartup.Configure(sut);

		// then
		Assert.False(sut.Enabled);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void respects_configuration_feature_flag(bool enabled) {
		using var sut = new SecondLevelIndexingPlugin();

		IConfigurationBuilder configBuilder = new ConfigurationBuilder();
		if (enabled)
			configBuilder = configBuilder.AddInMemoryCollection(new Dictionary<string, string?> {
				{ $"{KurrentConfigurationConstants.Prefix}:SecondLevelIndexing:Enabled", "true" },
			});

		// when
		TestPluginStartup.Configure(sut, configBuilder);

		// then
		Assert.Equal(enabled, sut.Enabled);
	}
}
