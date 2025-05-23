// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Confluent.Kafka;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Plugins.TestHelpers;
using Microsoft.Extensions.Configuration;
using Assert = Xunit.Assert;

namespace KurrentDB.SecondaryIndexing.Tests;

public class LogV2SecondaryIndexingPluginTests : SecondaryIndexingPluginTests<string>;
public class LogV3SecondaryIndexingPluginTests : SecondaryIndexingPluginTests<uint>;

public abstract class SecondaryIndexingPluginTests<TStreamId> {
	[Fact]
	public void is_disabled_by_default() {
		using var sut = new SecondaryIndexingPlugin<TStreamId>(new VirtualStreamReader());

		// when
		using var app = TestPluginStartup.Configure(sut);

		// then
		Assert.False(sut.Enabled);
	}

	[Theory(Skip = "TODO: make a proper configuration")]
	[InlineData(true, true, true)]
	[InlineData(true, false, true)]
	[InlineData(false, false, false)]
	[InlineData(false, true, false)]
	[InlineData(null, false, false)]
	[InlineData(null, true, true)]
	public void respects_configuration_feature_flag_and_dev_mode(bool? pluginEnabled, bool devMode, bool expected) {
		using var sut = new SecondaryIndexingPlugin<TStreamId>(new VirtualStreamReader());

		var configuration = new Dictionary<string, string?> {
			{$"{KurrentConfigurationKeys.Prefix}:Dev", devMode.ToString().ToLower()}
		};

		if (pluginEnabled.HasValue)
			configuration.Add(
				$"{KurrentConfigurationConstants.Prefix}:SecondaryIndexing:Enabled",
				pluginEnabled.Value.ToString().ToLower()
			);

		var configBuilder = new ConfigurationBuilder()
			.AddInMemoryCollection(configuration);

		// when
		using var app = TestPluginStartup.Configure(sut, configBuilder);

		// then
		Assert.Equal(expected, sut.Enabled);
	}
}
