// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using EventStore.Plugins.Subsystems;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Services.Storage.InMemory;
using Microsoft.Extensions.Configuration;

namespace KurrentDB.SecondaryIndexing;

public interface ISecondaryIndexingPlugin : ISubsystemsPlugin {
	IEnumerable<IVirtualStreamReader> IndicesVirtualStreamReaders { get; }
}

internal class SecondaryIndexingPlugin(IEnumerable<IVirtualStreamReader>? indexingVirtualStreamReaders = null) : SubsystemsPlugin(name: "secondary-indexing"), ISecondaryIndexingPlugin {
	public IEnumerable<IVirtualStreamReader> IndicesVirtualStreamReaders { get; } = indexingVirtualStreamReaders ?? [];

	public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
		var enabledOption =
			configuration.GetValue<bool?>($"{KurrentConfigurationKeys.Prefix}:SecondaryIndexing:Enabled");
		var devMode = configuration.GetValue($"{KurrentConfigurationKeys.Prefix}:Dev", defaultValue: false);

		// Enabled by default only in the dev mode
		// TODO: Change it to be enabled by default when work on secondary indexing is finished
		bool enabled = enabledOption ?? devMode;

		return enabled
			? (true, "")
			: (false,
				$"To enable Second Level Indexing Set '{KurrentConfigurationKeys.Prefix}:SecondaryIndexing:Enabled' to 'true'");
	}
}
