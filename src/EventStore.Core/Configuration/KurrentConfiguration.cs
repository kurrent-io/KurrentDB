// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Collections;
using EventStore.Common.Configuration;
using EventStore.Core.Configuration.Sources;
using EventStore.Core.Util;
using Microsoft.Extensions.Configuration;

namespace EventStore.Core.Configuration;

public static class KurrentConfiguration {
	public static IConfigurationRoot Build(LocationOptionWithLegacyDefault[] optionsWithLegacyDefaults, string[] args, IDictionary environment) {

		// resolve the main configuration file
		var configFile = ResolveConfigurationFile(optionsWithLegacyDefaults, args, environment);

		// Create a single IConfiguration object that contains the whole configuration, including
		// plugin configuration. We will add it to the DI and make it available to the plugins.
		//
		// Three json files are loaded explicitly for backwards compatibility
		// - metricsconfig.json needs loading into the KurrentDB:Metrics section.
		// - kestrelsettings.json is not located in a config/ directory
		// - logconfig.json is not located in a config/ directory
		//
		// The EventStore configuration section is transformed to KurrentDB by the EventStore configuration providers
		// to maintain backwards compatibility
		var builder = new ConfigurationBuilder()
			// we should be able to stop doing this soon as long as we bind the options automatically
			.AddKurrentDefaultValues()
			// check for the existence of KurrentDB default locations
			// if they do not exist, use the EventStore ones if the EventStore location exists.
			.AddLegacyDefaultLocations(optionsWithLegacyDefaults)
			.AddKurrentYamlConfigFile(configFile.Path, configFile.Optional)

			.AddSection($"{KurrentConfigurationKeys.Prefix}:Metrics",
				x => x.AddKurrentConfigFile("metricsconfig.json", true, true))

			// The other config files are added to the root, and must put themselves in the appropriate sections
			.AddKurrentConfigFile("kestrelsettings.json", true, true)
			.AddKurrentConfigFile("logconfig.json", true, true)

			// Load all json files in the  `config` subdirectory (if it exists) of each configuration
			// directory. We use the subdirectory to ensure that we only load configuration files.
			.AddLegacyEventStoreConfigFiles("*.json")
			.AddKurrentConfigFiles("*.json")

#if DEBUG
			// load all json files in the current directory
			.AddJsonFile("appsettings.json", true)
			.AddJsonFile("appsettings.Development.json", true)
#endif

			.AddEnvironmentVariables()
			.AddLegacyEventStoreEnvironmentVariables(environment)
			.AddKurrentEnvironmentVariables(environment)

			// follows current behaviour yet Env Vars should take precedence
			// to handle real-world deployment pipeline scenarios
			.AddLegacyEventStoreCommandLine(args)
			.AddKurrentCommandLine(args);

		return builder.Build();
	}

	public static IConfigurationRoot Build(LocationOptionWithLegacyDefault[] optionsWithLegacyDefaults, params string[] args) {
		var environment = Environment.GetEnvironmentVariables();
		return Build(optionsWithLegacyDefaults, args, environment);
	}

	private static (string Path, bool Optional) ResolveConfigurationFile(
		LocationOptionWithLegacyDefault[] optionsWithLegacyDefaults, string[] args, IDictionary environment) {
		var configuration = new ConfigurationBuilder()
			.AddLegacyEventStoreEnvironmentVariables(environment)
			.AddKurrentEnvironmentVariables(environment)
			.AddLegacyEventStoreCommandLine(args)
			.AddKurrentCommandLine(args)
			.Build();

		var configFilePath = configuration.GetValue<string?>($"{KurrentConfigurationKeys.Prefix}:Config");

		if (configFilePath is not null) {
			// if the user has specified a config file make it non optional
			return (configFilePath, false);
		}

		// rebuild the configuration just with the defaults
		configuration = new ConfigurationBuilder()
			.AddKurrentDefaultValues()
			.AddLegacyDefaultLocations(optionsWithLegacyDefaults)
			.Build();

		configFilePath = configuration.GetValue<string?>($"{KurrentConfigurationKeys.Prefix}:Config");
		// this is a default, so it can be optional
		return configFilePath is not null
			? (configFilePath, true)
			: (DefaultFiles.DefaultConfigPath, true);
	}

	public static IConfigurationBuilder AddKurrentYamlConfigFile(
		this IConfigurationBuilder builder,
		string path,
		bool optional) =>
		builder.AddSection(
			KurrentConfigurationKeys.Prefix,
			x => x.AddYamlFile(
				path: path,
				optional: optional,
				reloadOnChange: true));
}
