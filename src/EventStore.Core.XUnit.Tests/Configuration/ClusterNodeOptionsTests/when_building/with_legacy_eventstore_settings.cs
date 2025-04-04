// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using EventStore.Core.Configuration.Sources;
using EventStore.Core.Services;
using EventStore.Core.Tests;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using NUnit.Framework;

namespace EventStore.Core.XUnit.Tests.Configuration.ClusterNodeOptionsTests.when_building;

[TestFixture]
public class with_legacy_eventstore_settings : SingleNodeScenario<LogFormat.V2, string> {
	protected override ClusterVNodeOptions WithOptions(ClusterVNodeOptions options) =>
		options with {
		};

	private IConfigurationRoot _configurationRoot;

	[Test]
	public void should_return_error_when_default_password_options_pass_through_eventstore_command_line() {
		var args = new[] {
			$"--{KurrentConfigurationKeys.LegacyEventStorePrefix}:DefaultAdminPassword=Admin2023#",
			$"--{KurrentConfigurationKeys.LegacyEventStorePrefix}:DefaultOpsPassword=Ops2023#"
		};

		_configurationRoot = new ConfigurationBuilder()
			.AddKurrentDefaultValues(new Dictionary<string, object> {
				[nameof(ClusterVNodeOptions.DefaultUser.DefaultAdminPassword)] = SystemUsers.DefaultAdminPassword,
				[nameof(ClusterVNodeOptions.DefaultUser.DefaultOpsPassword)] = SystemUsers.DefaultOpsPassword
			})
			.AddLegacyEventStoreCommandLine(args)
			.Build();

		var clusterVNodeOptions = ClusterVNodeOptions.FromConfiguration(_configurationRoot);

		Assert.NotNull(clusterVNodeOptions.CheckForEnvironmentOnlyOptions());
	}

	[Test]
	public void should_return_warnings_when_options_are_provided_by_legacy_eventstore_sources() {
		var eventStorePrefix = KurrentConfigurationKeys.LegacyEventStorePrefix;
		var kurrentPrefix = KurrentConfigurationKeys.Prefix;

		// json config file
		var fileName = "test.eventstore.json";
		var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration", fileName);
		var directory = Path.GetDirectoryName(filePath);
		Assert.NotNull(directory);

		// environment variables
		IDictionary environmentVariables = new Dictionary<string, string>();
		environmentVariables.Add($"{eventStorePrefix.ToUpper()}_NODE_PORT", "111");
		environmentVariables.Add($"{kurrentPrefix.ToUpper()}_REPLICATION_PORT", "333");

		// command line
		var args = new[] { $"--{eventStorePrefix}:RunProjections=All", $"--{kurrentPrefix}:MemDb=true" };

		_configurationRoot = new ConfigurationBuilder()
			.AddKurrentDefaultValues()
			.AddLegacyEventStoreJsonFile(config => {
				config.FileProvider = new PhysicalFileProvider(directory) {
					UseActivePolling = false,
					UsePollingFileWatcher = false
				};
				config.OnLoadException = _ =>
					Assert.Fail($"Could not find test config file '{fileName}' in '{directory}'");
				config.Path = fileName;
			})
			.AddLegacyEventStoreEnvironmentVariables(environmentVariables)
			.AddLegacyEventStoreCommandLine(args)
			.Build();

		var clusterVNodeOptions = ClusterVNodeOptions.FromConfiguration(_configurationRoot);
		var warnings = clusterVNodeOptions.CheckForLegacyEventStoreConfiguration();
		var expected = new[] {
			$"\"Autoscavenge\" Provided by: Event Store Json File. The \"EventStore\" configuration root has been deprecated, use \"{kurrentPrefix}\" instead.",
			$"\"Connectors\" Provided by: Event Store Json File. The \"EventStore\" configuration root has been deprecated, use \"{kurrentPrefix}\" instead.",
			$"\"Licensing\" Provided by: Event Store Json File. The \"EventStore\" configuration root has been deprecated, use \"{kurrentPrefix}\" instead.",
			$"\"NodePort\" Provided by: Event Store Environment Variables. The \"EventStore\" configuration root has been deprecated, use \"{kurrentPrefix}\" instead.",
			$"\"RunProjections\" Provided by: Event Store Command Line. The \"EventStore\" configuration root has been deprecated, use \"{kurrentPrefix}\" instead."
		};
		Assert.AreEqual(expected, warnings);
	}

	[Test]
	public void should_return_null_when_default_password_options_pass_through_environment_variables() {
		var prefix = KurrentConfigurationKeys.LegacyEventStorePrefix.ToUpper();
		var args = Array.Empty<string>();
		IDictionary environmentVariables = new Dictionary<string, string>();
		environmentVariables.Add($"{prefix}_DEFAULT_ADMIN_PASSWORD", "Admin#");
		environmentVariables.Add($"{prefix}_DEFAULT_OPS_PASSWORD", "Ops#");

		_configurationRoot = new ConfigurationBuilder()
			.AddKurrentDefaultValues(new Dictionary<string, object> {
				[nameof(ClusterVNodeOptions.DefaultUser.DefaultAdminPassword)] = SystemUsers.DefaultAdminPassword,
				[nameof(ClusterVNodeOptions.DefaultUser.DefaultOpsPassword)] = SystemUsers.DefaultOpsPassword
			})
			.AddKurrentCommandLine(args)
			.AddLegacyEventStoreEnvironmentVariables(environmentVariables)
			.Build();

		var clusterVNodeOptions = ClusterVNodeOptions.FromConfiguration(_configurationRoot);

		Assert.Null(clusterVNodeOptions.CheckForEnvironmentOnlyOptions());
	}

	[Test]
	public void ignores_subsection_arguments() {
		var prefix = KurrentConfigurationKeys.LegacyEventStorePrefix;
		var configuration = new ConfigurationBuilder()
			// we should be able to stop doing this soon as long as we bind the options automatically
			.AddKurrentDefaultValues()
			.AddLegacyEventStoreEnvironmentVariables(
				($"{prefix.ToUpper()}__METRICS__X", "xxx"),
				($"{prefix.ToUpper()}__PLUGINS__Y", "yyy")
			)
			.AddLegacyEventStoreCommandLine(
				$"--{prefix}:Metrics:A aaa " +
				$"--{prefix}:Plugins:B bbb"
			)
			.Build();

		var options = ClusterVNodeOptions.FromConfiguration(configuration);

		options.Unknown.Options.Should().BeEmpty();
	}
}
