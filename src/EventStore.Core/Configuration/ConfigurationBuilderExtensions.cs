// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.IO;
using System.Linq;
using EventStore.Common.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace EventStore.Core.Configuration;

public static class ConfigurationBuilderExtensions {

	public static IConfigurationBuilder AddKurrentConfigFile(this IConfigurationBuilder builder, string configFilePath,
		bool optional = false, bool reloadOnChange = false) {
		if (!Locations.TryLocateConfigFile(configFilePath, out var directory, out var fileName)) {
			if (optional)
				return builder;

			throw new FileNotFoundException(
				$"Could not find {configFilePath} in the following directories: {string.Join(", ", Locations.GetPotentialConfigurationDirectories())}");
		}

		builder.AddJsonFile(config => {
			config.Optional = optional;
			config.FileProvider = new PhysicalFileProvider(directory) {
				UseActivePolling = reloadOnChange,
				UsePollingFileWatcher = reloadOnChange
			};
			config.OnLoadException = context => Serilog.Log.Error(context.Exception, "err");
			config.Path = fileName;
			config.ReloadOnChange = reloadOnChange;
		});

		return builder;
	}

	public static IConfigurationBuilder AddKurrentConfigFiles(this IConfigurationBuilder builder, string subdirectory,
		string pattern) {
		AddConfigFiles(subdirectory, pattern, (file) =>
			builder.AddKurrentConfigFile(file, optional: true, reloadOnChange: true));
		return builder;
	}

	public static void AddConfigFiles(string subdirectory,
		string pattern, Action<string> addConfigFile) {
		// when searching for a file we check the directories in forward order until we find it
		// so when adding all the files we apply them in reverse order to keep the same precedence
		foreach (var directory in Locations.GetPotentialConfigurationDirectories().Reverse()) {
			var configDirectory = Path.Combine(directory, subdirectory);
			if (!Directory.Exists(configDirectory))
				continue;

			foreach (var configFile in Directory.EnumerateFiles(configDirectory, pattern).Order()) {
				addConfigFile(configFile);
			}
		}
	}

	public static IConfigurationBuilder AddKurrentConfigFiles(this IConfigurationBuilder builder, string pattern) =>
		builder.AddKurrentConfigFiles("config", pattern);
}
