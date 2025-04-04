// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System.Collections.Generic;
using System.IO.Abstractions;
using EventStore.Common.Utils;
using EventStore.Core.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;

namespace EventStore.Core.Configuration.Sources;

public class EventStoreDefaultLocationsProvider(MemoryConfigurationSource source)
	: MemoryConfigurationProvider(source);

public class EventStoreDefaultLocationsSource(LocationOptionWithLegacyDefault[] defaultLocations, IFileSystem fileSystem) : IConfigurationSource {
	public IConfigurationProvider Build(IConfigurationBuilder builder) {
		var overrideLocations = new List<KeyValuePair<string, string?>>();
		foreach (var location in defaultLocations) {
			if (location.IsFile) {
				if (!fileSystem.File.Exists(location.KurrentPath) && fileSystem.File.Exists(location.LegacyPath)) {
					overrideLocations.Add(new KeyValuePair<string, string?>(location.OptionKey, location.LegacyPath));
				}
			} else {
				if (!fileSystem.Directory.Exists(location.KurrentPath) && fileSystem.Directory.Exists(location.LegacyPath)) {
					overrideLocations.Add(new KeyValuePair<string, string?>(location.OptionKey, location.LegacyPath));
				}
			}
		}
		return new EventStoreDefaultLocationsProvider(new MemoryConfigurationSource { InitialData = overrideLocations });
	}
}

public static class EventStoreDefaultLocationsConfigurationExtensions {
	public static IConfigurationBuilder AddLegacyDefaultLocations(this IConfigurationBuilder builder,
		LocationOptionWithLegacyDefault[] defaultLocations, IFileSystem? fileSystem = null) {
		return builder.Add(new EventStoreDefaultLocationsSource(defaultLocations, fileSystem ?? new FileSystem()));
	}
}

public record LocationOptionWithLegacyDefault(string OptionKey, string KurrentPath, string LegacyPath, bool IsFile) {
	public static LocationOptionWithLegacyDefault[] SupportedLegacyLocations => [
		new($"{KurrentConfigurationKeys.Prefix}:Config",
			DefaultFiles.DefaultConfigPath,
			DefaultFiles.LegacyEventStoreConfigPath,
			true),
		new($"{KurrentConfigurationKeys.Prefix}:Db",
			Locations.DefaultDataDirectory,
			Locations.LegacyDataDirectory,
			false),
		new($"{KurrentConfigurationKeys.Prefix}:Log",
			Locations.DefaultLogDirectory,
			Locations.LegacyLogDirectory,
			false)
	];
}
