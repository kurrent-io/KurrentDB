// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Configuration.Sources;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Configuration;

public class CommandLineConfigurationSourceTests {
	[Theory]
	[InlineData(new[] { "--stream_info_cache_capacity=99" }, "KurrentDB:StreamInfoCacheCapacity", "99")]
	[InlineData(new[] { "--stream_info_cache_capacity", "99" }, "KurrentDB:StreamInfoCacheCapacity", "99")]
	[InlineData(new[] { "--stream-info-cache-capacity=99" }, "KurrentDB:StreamInfoCacheCapacity", "99")]
	[InlineData(new[] { "--stream-info-cache-capacity", "99" }, "KurrentDB:StreamInfoCacheCapacity", "99")]
	[InlineData(new[] { "--KurrentDB:StreamInfoCacheCapacity=99" }, "KurrentDB:StreamInfoCacheCapacity", "99")]
	[InlineData(new[] { "--KurrentDB:StreamInfoCacheCapacity", "99" }, "KurrentDB:StreamInfoCacheCapacity", "99")]
	[InlineData(new[] { "--KurrentDB:Stream-Info-Cache-Capacity=99" }, "KurrentDB:StreamInfoCacheCapacity", "99")]
	[InlineData(new[] { "--KurrentDB:Stream-Info-Cache-Capacity", "99" }, "KurrentDB:StreamInfoCacheCapacity", "99")]
	[InlineData(new[] { "--KurrentDB:Stream_Info_Cache_Capacity=99" }, "KurrentDB:StreamInfoCacheCapacity", "99")]
	[InlineData(new[] { "--KurrentDB:Stream_Info_Cache_Capacity", "99" }, "KurrentDB:StreamInfoCacheCapacity", "99")]
	[InlineData(new[] { "--Kurrentdb:Stream_Info_Cache_Capacity=99" }, "KurrentDB:StreamInfoCacheCapacity", "99")]
	public void AddsArguments(string[] arguments, string normalizedKey, string expectedValue) {
		// Act
		var configuration = new ConfigurationBuilder()
			.AddKurrentCommandLine(arguments)
			.Build();

		// Assert
		configuration.GetValue<string>(normalizedKey).Should().Be(expectedValue);
	}

	static IConfiguration BuildConfiguration(params string[] args) =>
		new ConfigurationBuilder()
			.AddKurrentCommandLine(args)
			.Build()
			.GetSection(KurrentConfigurationKeys.Prefix);

	[Fact]
	public void normalize_keys() {
		var configuration = BuildConfiguration("--cluster-size", "3", "--log", "/tmp/kurrentdb/logs");
		Assert.Equal(3, configuration.GetValue<int>("ClusterSize"));
		Assert.Equal("/tmp/kurrentdb/logs", configuration.GetValue<string>("Log"));
	}

	[Fact]
	public void normalize_keys_boolean_plus_sign() {
		var configuration = BuildConfiguration("--whatever+");
		Assert.Equal("true", configuration.GetValue<string>("Whatever"));
	}

	[Fact]
	public void normalize_keys_boolean_negative_sign() {
		var configuration = BuildConfiguration("--whatever-");
		Assert.Equal("false", configuration.GetValue<string>("Whatever"));
	}

	[Fact]
	public void normalize_keys_boolean_no_value() {
		var configuration = BuildConfiguration("--whatever");
		Assert.Equal("true", configuration.GetValue<string>("Whatever"));
	}

	[Fact]
	public void normalize_keys_equals() {
		var configuration = BuildConfiguration("--cluster-size=3");
		Assert.Equal(3, configuration.GetValue<int>("ClusterSize"));
	}

	[Fact]
	public void normalize_keys_one_dash() {
		var configuration = BuildConfiguration("-cluster-size=3");
		Assert.Equal(3, configuration.GetValue<int>("ClusterSize"));
	}
}
