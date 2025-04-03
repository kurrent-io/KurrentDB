// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Net;
using EventStore.Common.Exceptions;
using EventStore.Common.Options;
using EventStore.Core.Configuration;
using EventStore.Core.Configuration.Sources;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Configuration;

public class ClusterVNodeOptionsTests {
	private const string KurrentPrefix = KurrentConfigurationKeys.Prefix;
	private string KurrentEnvVarPrefix => KurrentConfigurationKeys.Prefix.ToUpper();
	private const string EventStorePrefix = KurrentConfigurationKeys.LegacyEventStorePrefix;
	private string EventStoreEnvVarPrefix => KurrentConfigurationKeys.LegacyEventStorePrefix.ToUpper();
	static ClusterVNodeOptions GetOptions(string args) {
		var configuration = KurrentConfiguration.Build(
			LocationOptionWithLegacyDefault.SupportedLegacyLocations, args.Split());
		return ClusterVNodeOptions.FromConfiguration(configuration);
	}

	[Fact]
	public void builds_proper() {
		var options = new ClusterVNodeOptions();
	}

	[Fact]
	public void confirm_suggested_option() {
		var options = GetOptions("--cluster-sze 3");

		var (key, value) = options.Unknown.Options[0];
		Assert.Equal("ClusterSze", key);
		Assert.Equal("ClusterSize", value);
	}

	[Fact]
	public void when_we_dont_have_that_option() {
		var options = GetOptions("--something-that-is-wildly-off 3");

		var (key, value) = options.Unknown.Options[0];
		Assert.Equal("SomethingThatIsWildlyOff", key);
		Assert.Equal("", value);
	}

	[Fact]
	public void valid_parameters() {
		var options = GetOptions("--cluster-size 3");

		var values = options.Unknown.Options;
		Assert.Empty(values);
	}

	[Fact]
	public void unknown_options_suggests_option_with_four_characters_off() {
		var options = GetOptions("--cluse-ie 3");

		var (key, value) = options.Unknown.Options[0];
		Assert.Equal("CluseIe", key);
		Assert.Equal("ClusterSize", value);
	}

	[Fact]
	public void print_help_text() {
		var helpText = ClusterVNodeOptions.HelpText;
		helpText.Should().NotBeEmpty();
	}

	[Fact]
	public void unknown_options_ignores_subsection_arguments() {
		var configuration = new ConfigurationBuilder()
			.AddKurrentDefaultValues()
			.AddLegacyEventStoreEnvironmentVariables(
				($"{EventStoreEnvVarPrefix}__METRICS__Q", "qqq"),
				($"{EventStoreEnvVarPrefix}__PLUGINS__R", "rrr")
			)
			.AddKurrentEnvironmentVariables(
				($"{KurrentEnvVarPrefix}__METRICS__X", "xxx"),
				($"{KurrentEnvVarPrefix}__PLUGINS__Y", "yyy")
			)
			.AddLegacyEventStoreCommandLine(
				$"--{EventStorePrefix}:Metrics:A aaa " +
				$"--{EventStorePrefix}:Plugins:B bbb"
				)
			.AddKurrentCommandLine(
				$"--{KurrentPrefix}:Metrics:C ccc " +
				$"--{KurrentPrefix}:Plugins:D ddd"
			)
			.Build();

		var options = ClusterVNodeOptions.FromConfiguration(configuration);

		options.Unknown.Options.Should().BeEmpty();
	}

	[Fact]
	public void unknown_options_ignores_repeated_keys_from_other_sources() {
		Environment.SetEnvironmentVariable($"{KurrentEnvVarPrefix}__CLUSTER_SIZE", "3");

		var configuration = new ConfigurationBuilder()
			.AddEnvironmentVariables()
			.AddKurrentEnvironmentVariables()
			.Build();

		var options = ClusterVNodeOptions.FromConfiguration(configuration);

		options.Unknown.Options.Should().BeEmpty();
	}

	[Fact]
	public void validation_should_return_error_when_default_password_options_pass_through_command_line() {
		var configuration =  new ConfigurationBuilder()
			.AddKurrentDefaultValues()
			.AddLegacyEventStoreCommandLine(
				$"--{EventStorePrefix}:DefaultAdminPassword=Admin#")
			.AddKurrentCommandLine(
				"--DefaultOpsPassword=Ops#")
			.Build();

		var options = ClusterVNodeOptions.FromConfiguration(configuration);

		var result = options.CheckForEnvironmentOnlyOptions();

		result.Should().BeEquivalentTo(
			"\"DefaultAdminPassword\" Provided by: Event Store Command Line. " +
			"The Admin user password can only be set using Environment Variables" + Environment.NewLine +
			"\"DefaultOpsPassword\" Provided by: Command Line. " +
			"The Ops user password can only be set using Environment Variables" + Environment.NewLine);
	}

	[Fact]
	public void validation_should_return_null_when_default_password_options_pass_through_environment_variables() {
		var configuration = new ConfigurationBuilder()
			.AddKurrentDefaultValues()
			.AddKurrentEnvironmentVariables(
				($"{KurrentEnvVarPrefix}_DEFAULT_ADMIN_PASSWORD", "Admin#"),
				($"{KurrentEnvVarPrefix}_DEFAULT_OPS_PASSWORD", "Ops#")
			)
			.AddKurrentCommandLine()
			.Build();

		var options = ClusterVNodeOptions.FromConfiguration(configuration);

		var result = options.CheckForEnvironmentOnlyOptions();

		result.Should().BeNull();
	}

	[Fact]
	public void validation_should_return_null_when_default_password_options_pass_through_legacy_eventstore_environment_variables() {
		var configuration = new ConfigurationBuilder()
			.AddKurrentDefaultValues()
			.AddLegacyEventStoreEnvironmentVariables(
				($"{EventStoreEnvVarPrefix}_DEFAULT_ADMIN_PASSWORD", "Admin#"),
				($"{EventStoreEnvVarPrefix}_DEFAULT_OPS_PASSWORD", "Ops#")
			)
			.AddKurrentCommandLine()
			.Build();

		var options = ClusterVNodeOptions.FromConfiguration(configuration);

		var result = options.CheckForEnvironmentOnlyOptions();

		result.Should().BeNull();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void can_set_gossip_seed_values(bool useEventStorePrefix) {
		EndPoint[] endpoints = [
			new IPEndPoint(IPAddress.Loopback, 1113),
			new DnsEndPoint("some-host", 1114),
			new DnsEndPoint("127.0.1.15", 1115)
		];

		var values = string.Join(",", endpoints.Select(x => $"{x}"));

		var builder = new ConfigurationBuilder();
		if (useEventStorePrefix)
			builder.AddLegacyEventStoreEnvironmentVariables(($"{EventStoreEnvVarPrefix}_GOSSIP_SEED", values));
		else
			builder.AddKurrentEnvironmentVariables(($"{KurrentEnvVarPrefix}_GOSSIP_SEED", values));
		var config = builder.Build();

		var options = ClusterVNodeOptions.FromConfiguration(config);

		options.Cluster.GossipSeed.Should().BeEquivalentTo(endpoints);
	}

	[Fact]
	public void can_set_gossip_seed_values_via_array() {
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection([
				new($"{KurrentPrefix}:GossipSeed:0", "127.0.0.1:1113"),
				new($"{KurrentPrefix}:GossipSeed:1", "some-host:1114"),
			])
			.Build();

		var options = ClusterVNodeOptions.FromConfiguration(config);

		options.Cluster.GossipSeed.Should().BeEquivalentTo(new EndPoint[] {
			new IPEndPoint(IPAddress.Loopback, 1113),
			new DnsEndPoint("some-host", 1114),
		});
	}

	[Theory]
	[InlineData(true, "127.0.0.1", "You must specify the ports in the gossip seed.")]
	[InlineData(true, "127.0.0.1:3.1415", "Invalid format for gossip seed port: 3.1415.")]
	[InlineData(true, "hostA;hostB", "Invalid delimiter for gossip seed value: hostA;hostB.")]
	[InlineData(true, "hostA\thostB", "Invalid delimiter for gossip seed value: hostA\thostB.")]
	[InlineData(false, "127.0.0.1", "You must specify the ports in the gossip seed.")]
	[InlineData(false, "127.0.0.1:3.1415", "Invalid format for gossip seed port: 3.1415.")]
	[InlineData(false, "hostA;hostB", "Invalid delimiter for gossip seed value: hostA;hostB.")]
	[InlineData(false, "hostA\thostB", "Invalid delimiter for gossip seed value: hostA\thostB.")]
	public void reports_gossip_seed_errors(bool useEventStorePrefix, string gossipSeed, string expectedError) {
		var builder = new ConfigurationBuilder();
		if (useEventStorePrefix)
			builder.AddLegacyEventStoreEnvironmentVariables(($"{EventStoreEnvVarPrefix}_GOSSIP_SEED", gossipSeed));
		else
			builder.AddKurrentEnvironmentVariables(($"{KurrentEnvVarPrefix}_GOSSIP_SEED", gossipSeed));
		var config = builder.Build();

		var ex = Assert.Throws<InvalidConfigurationException>(() =>
			ClusterVNodeOptions.FromConfiguration(config));

		Assert.Equal(
			$"Failed to convert configuration value at '{KurrentPrefix}:GossipSeed' to type 'System.Net.EndPoint[]'. " + expectedError,
			ex.Message);
	}

	[Theory]
	[InlineData(true, "127.0.0.1.0", "An invalid IP address was specified.")]
	[InlineData(false, "127.0.0.1.0", "An invalid IP address was specified.")]
	public void reports_ip_address_errors(bool useEventStorePrefix, string nodeIp, string expectedError) {
		var builder = new ConfigurationBuilder();
		if (useEventStorePrefix)
			builder.AddLegacyEventStoreEnvironmentVariables(($"{EventStoreEnvVarPrefix}_NODE_IP", nodeIp));
		else
			builder.AddKurrentEnvironmentVariables(($"{KurrentEnvVarPrefix}_NODE_IP", nodeIp));
		var config = builder.Build();

		var ex = Assert.Throws<InvalidConfigurationException>(() =>
			ClusterVNodeOptions.FromConfiguration(config));

		Assert.Equal(
			$"Failed to convert configuration value at '{KurrentPrefix}:NodeIp' to type 'System.Net.IPAddress'. " + expectedError,
			ex.Message);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void can_set_node_ip(bool useEventStorePrefix) {
		var builder = new ConfigurationBuilder();
		if (useEventStorePrefix)
			builder.AddLegacyEventStoreEnvironmentVariables(($"{EventStoreEnvVarPrefix}_NODE_IP", "192.168.0.1"));
		else
			builder.AddKurrentEnvironmentVariables(($"{KurrentEnvVarPrefix}_NODE_IP", "192.168.0.1"));
		var config = builder.Build();

		var options = ClusterVNodeOptions.FromConfiguration(config);

		options.Interface.NodeIp.Should().Be(IPAddress.Parse("192.168.0.1"));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void can_set_cluster_size_from_env_vars(bool useEventStorePrefix) {
		var builder = new ConfigurationBuilder();
		if (useEventStorePrefix)
			builder.AddLegacyEventStoreEnvironmentVariables(($"{EventStoreEnvVarPrefix}_CLUSTER_SIZE", "23"));
		else
			builder.AddKurrentEnvironmentVariables(($"{KurrentEnvVarPrefix}_CLUSTER_SIZE", "23"));
		var config = builder.Build();

		var options = ClusterVNodeOptions.FromConfiguration(config);

		options.Cluster.ClusterSize.Should().Be(23);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void can_set_cluster_size_from_args(bool useEventStorePrefix) {
		var builder = new ConfigurationBuilder();
		if (useEventStorePrefix)
			builder.AddLegacyEventStoreCommandLine($"--{EventStorePrefix}:CLUSTER-SIZE=23");
		else
			builder.AddKurrentCommandLine("--CLUSTER-SIZE=23");
		var config = builder.Build();

		var options = ClusterVNodeOptions.FromConfiguration(config);

		options.Cluster.ClusterSize.Should().Be(23);
	}

	[Fact]
	public void can_set_cluster_size_from_config_file() {
		var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration", "test.kurrentdb.conf");

		var config = new ConfigurationBuilder()
			.AddKurrentYamlConfigFile(path, optional: false)
			.Build();

		var options = ClusterVNodeOptions.FromConfiguration(config);

		options.Cluster.ClusterSize.Should().Be(23);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void can_set_log_level_from_env_vars(bool useEventStorePrefix) {
		var builder = new ConfigurationBuilder();
		if (useEventStorePrefix)
			builder.AddLegacyEventStoreEnvironmentVariables(($"{EventStoreEnvVarPrefix}_LOG_LEVEL", LogLevel.Fatal.ToString()));
		else
			builder.AddKurrentEnvironmentVariables(($"{KurrentEnvVarPrefix}_LOG_LEVEL", LogLevel.Fatal.ToString()));
		var config = builder.Build();

		var options = ClusterVNodeOptions.FromConfiguration(config);

		options.Logging.LogLevel.Should().Be(LogLevel.Fatal);
	}

	[Fact]
	public void no_defaults_are_deprecated() {
		var config = new ConfigurationBuilder()
			.AddKurrentDefaultValues()
			.Build();

		var options = ClusterVNodeOptions.FromConfiguration(config);
		options.GetDeprecationWarnings().Should().BeNullOrEmpty();
	}

	[Fact]
	public void can_get_deprecation_warnings() {
		var config = new ConfigurationBuilder()
			.AddKurrentDefaultValues()
			.AddLegacyEventStoreEnvironmentVariables(($"{EventStoreEnvVarPrefix}_ENABLE_HISTOGRAMS", "true"))
			.AddKurrentEnvironmentVariables(($"{KurrentEnvVarPrefix}_ENABLE_ATOM_PUB_OVER_HTTP", "true"))
			.Build();

		var options = ClusterVNodeOptions.FromConfiguration(config);
		options.GetDeprecationWarnings().Should().Be(
			"The EnableHistograms setting has been deprecated as of version 24.10.0 and currently has no effect. Please contact Kurrent if this feature is of interest to you." +
			Environment.NewLine +
			"AtomPub over HTTP Interface has been deprecated as of version 20.6.0. It is recommended to use gRPC instead" +
			Environment.NewLine);
	}
}
