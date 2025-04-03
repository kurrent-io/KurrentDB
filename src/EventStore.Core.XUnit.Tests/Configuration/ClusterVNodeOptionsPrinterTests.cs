// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Collections.Generic;
using EventStore.Common.Options;
using EventStore.Core.Configuration;
using EventStore.Core.Configuration.Sources;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EventStore.Core.XUnit.Tests.Configuration;

public class ClusterVNodeOptionsPrinterTests {
	[Fact]
	public void prints_options() {
		var kurrentPrefix = KurrentConfigurationKeys.Prefix.ToUpper();
		var eventStorePrefix = KurrentConfigurationKeys.LegacyEventStorePrefix.ToUpper();
		var config = new ConfigurationBuilder()
			.AddKurrentDefaultValues(new Dictionary<string, string?> {
				{ "ChunkSize", "10000"},
				{ "ChunksCacheSize", "20000" },
				{ "ClusterSize", "1" },
				{ "UnsafeAllowSurplusNodes", "false" },
			})
			.AddLegacyEventStoreEnvironmentVariables(
				($"{eventStorePrefix}_WORKER_THREADS", "5"),
				($"{eventStorePrefix}_NODE_PRIORITY", "6"),
				($"{eventStorePrefix}_ANOTHER_UNKNOWN_OPTIONS", "7"))
			.AddKurrentEnvironmentVariables(
				($"{kurrentPrefix}_SOME_UNKNOWN_OPTION", "77"),
				($"{kurrentPrefix}_CLUSTER_GOSSIP_PORT", "99"),
				($"{kurrentPrefix}_UNSAFE_ALLOW_SURPLUS_NODES", "true"),
				($"{kurrentPrefix}_CONFIG", "/path/to/config/envvar"),
				($"{kurrentPrefix}_RUN_PROJECTIONS", "All"))
			.AddKurrentCommandLine(
				"--config", "/path/to/config/commandline",
				"--cluster-gossip-port=88",
				$"--default-ops-password={Guid.NewGuid()}")
			.Build();

		var loadedOptions = ClusterVNodeOptions.GetLoadedOptions(config);

		var output = ClusterVNodeOptionsPrinter.Print(loadedOptions);

		output.Should().Be(@"
MODIFIED OPTIONS:
    APPLICATION OPTIONS:
         CONFIG:                            /path/to/config/commandline (Command Line)
         WORKER THREADS:                    5 (Event Store Environment Variables)

    CLUSTER OPTIONS:
         CLUSTER GOSSIP PORT:               88 (Command Line)
         NODE PRIORITY:                     6 (Event Store Environment Variables)
         UNSAFE ALLOW SURPLUS NODES:        true (Environment Variables)

    DEFAULT USER OPTIONS:
         DEFAULT OPS PASSWORD:              ******** (Command Line)

    PROJECTION OPTIONS:
         RUN PROJECTIONS:                   All (Environment Variables)


DEFAULT OPTIONS:
    CLUSTER OPTIONS:
         CLUSTER SIZE:                      1 (<DEFAULT>)

    DATABASE OPTIONS:
         CHUNK SIZE:                        10000 (<DEFAULT>)
         CHUNKS CACHE SIZE:                 20000 (<DEFAULT>)
");
	}

	[Fact]
	public void loaded_options_do_not_contain_sensitive_values() {
		var secretText = Guid.NewGuid().ToString();

		var config = new ConfigurationBuilder()
			.AddKurrentDefaultValues()
			.AddLegacyEventStoreEnvironmentVariables(
				($"{KurrentConfigurationKeys.LegacyEventStorePrefix.ToUpper()}_DEFAULT_ADMIN_PASSWORD", secretText))
			.AddLegacyEventStoreCommandLine(
				$"--{KurrentConfigurationKeys.LegacyEventStorePrefix}:CertificatePassword", secretText)
			.AddKurrentCommandLine($"--default-ops-password={secretText}")
			.Build();

		var loadedOptions = ClusterVNodeOptions.GetLoadedOptions(config);

		var opsPassword = loadedOptions[$"{KurrentConfigurationKeys.Prefix}:DefaultOpsPassword"];
		var adminPassword = loadedOptions[$"{KurrentConfigurationKeys.Prefix}:DefaultAdminPassword"];
		var certPassword = loadedOptions[$"{KurrentConfigurationKeys.Prefix}:CertificatePassword"];

		opsPassword.DisplayValue.Should().BeEquivalentTo("********");
		adminPassword.DisplayValue.Should().BeEquivalentTo("********");
		certPassword.DisplayValue.Should().BeEquivalentTo("********");
	}

	[Fact]
	public void loaded_options_show_allowed_values() {
		var config = new ConfigurationBuilder()
			.AddKurrentDefaultValues()
			.Build();

		var loadedOptions = ClusterVNodeOptions.GetLoadedOptions(config);

		var option = loadedOptions[$"{KurrentConfigurationKeys.Prefix}:DbLogFormat"];

		option.Metadata.AllowedValues.Should().BeEquivalentTo(Enum.GetNames(typeof(DbLogFormat)));
	}

	[Fact]
	public void loaded_option_provided_by_another_source_shows_the_correct_source() {
		var kurrentPrefix = KurrentConfigurationKeys.Prefix;
		var eventStorePrefix = KurrentConfigurationKeys.LegacyEventStorePrefix;
		var config = new ConfigurationBuilder()
			.AddKurrentDefaultValues()
			.AddLegacyEventStoreEnvironmentVariables(
				($"{eventStorePrefix.ToUpper()}_CLUSTER_SIZE", "10"),
				($"{eventStorePrefix.ToUpper()}_NODE_PRIORITY", "11"))
			.AddKurrentEnvironmentVariables(
				($"{kurrentPrefix.ToUpper()}_CLUSTER_SIZE", "15"),
				($"{kurrentPrefix.ToUpper()}_LOG_LEVEL", "Fatal"),
				($"{kurrentPrefix.ToUpper()}_LOG_FORMAT", "json"))
			.AddLegacyEventStoreCommandLine(
				$"--{eventStorePrefix}:NodePort=17",
				$"--{eventStorePrefix}:ReadOnlyReplica=true")
			.AddKurrentCommandLine(
				$"--log-level=Information",
				$"--{kurrentPrefix}:NodePort=18")
			.Build();

		var loadedOptions = ClusterVNodeOptions.GetLoadedOptions(config);

		// default
		var insecure = loadedOptions[$"{kurrentPrefix}:Insecure"];
		insecure.DisplayValue.Should().BeEquivalentTo("false");
		insecure.SourceDisplayName.Should().BeEquivalentTo("<DEFAULT>");

		// legacy EventStore environment variables
		var nodePriority = loadedOptions[$"{kurrentPrefix}:NodePriority"];
		nodePriority.DisplayValue.Should().BeEquivalentTo("11");
		nodePriority.SourceDisplayName.Should().BeEquivalentTo("Event Store Environment Variables");

		// environment variables
		var clusterSize = loadedOptions[$"{kurrentPrefix}:ClusterSize"];
		clusterSize.DisplayValue.Should().BeEquivalentTo("15");
		clusterSize.SourceDisplayName.Should().BeEquivalentTo("Environment Variables");

		// legacy EventStore command line
		var readOnly = loadedOptions[$"{kurrentPrefix}:ReadOnlyReplica"];
		readOnly.DisplayValue.Should().BeEquivalentTo("true");
		readOnly.SourceDisplayName.Should().BeEquivalentTo("Event Store Command Line");

		// command line
		var logLevel = loadedOptions[$"{kurrentPrefix}:LogLevel"];
		logLevel.DisplayValue.Should().BeEquivalentTo("Information");
		logLevel.SourceDisplayName.Should().BeEquivalentTo("Command Line");

		// command line - override legacy EventStore
		var nodePort = loadedOptions[$"{kurrentPrefix}:NodePort"];
		nodePort.DisplayValue.Should().BeEquivalentTo("18");
		nodePort.SourceDisplayName.Should().BeEquivalentTo("Command Line");
	}
}
