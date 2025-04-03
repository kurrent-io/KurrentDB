// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Linq;
using EventStore.Common.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using static System.StringComparer;

namespace EventStore.Core.Configuration.Sources;

public class EventStoreJsonFileConfigurationProvider(EventStoreJsonFileConfigurationSource source)
	: JsonConfigurationProvider(source) {

	public override void Load() {
		base.Load();
		Data = Data.Keys.ToDictionary(
			KurrentConfigurationKeys.NormalizeEventStorePrefix,
			x => Data[x], OrdinalIgnoreCase
		);
	}
}

public class EventStoreJsonFileConfigurationSource : JsonConfigurationSource {
	public override IConfigurationProvider Build(IConfigurationBuilder builder) {
		EnsureDefaults(builder);
		return new EventStoreJsonFileConfigurationProvider(this);
	}
}

public static class EventStoreJsonFileConfigurationExtensions {
	public static IConfigurationBuilder AddLegacyEventStoreJsonFile(this IConfigurationBuilder builder, Action<EventStoreJsonFileConfigurationSource> configureSource) =>
		builder.Add(configureSource);

	public static IConfigurationBuilder AddLegacyEventStoreConfigFile(this IConfigurationBuilder builder, string configFilePath,
		bool optional = false, bool reloadOnChange = false) {
		if (!Locations.TryLocateConfigFile(configFilePath, out var directory, out var fileName)) {
			if (optional)
				return builder;

			throw new FileNotFoundException(
				$"Could not find {configFilePath} in the following directories: {string.Join(", ", Locations.GetPotentialConfigurationDirectories())}");
		}

		builder.AddLegacyEventStoreJsonFile(config => {
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

	public static IConfigurationBuilder AddLegacyEventStoreConfigFiles(this IConfigurationBuilder builder, string pattern) {
		ConfigurationBuilderExtensions.AddConfigFiles("config", pattern, (file) =>
			builder.AddLegacyEventStoreConfigFile(file, optional: true, reloadOnChange: true));
		return builder;
	}
}
