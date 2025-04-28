// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.POC.IO.Core;
using Microsoft.Extensions.Configuration;

namespace KurrentDB.SecondLevelIndexing;

public interface ISecondLevelIndexingPlugin : IConnectedSubsystemsPlugin {
	IEnumerable<IVirtualStreamReader> IndexingVirtualStreamReaders { get; }
}

public class SecondLevelIndexingPlugin() : SubsystemsPlugin(name: "second-level-indexing"), ISecondLevelIndexingPlugin {
	public IEnumerable<IVirtualStreamReader> IndexingVirtualStreamReaders { get; } = [new DummyVirtualStreamReader("$idx-test")];

	public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
		var enabledOption =
			configuration.GetValue<bool?>($"{KurrentConfigurationKeys.Prefix}:SecondLevelIndexing:Enabled");
		var devMode = configuration.GetValue($"{KurrentConfigurationKeys.Prefix}:Dev", defaultValue: false);

		// Enabled by default only in the dev mode
		// TODO: Change it to be enabled by default when work on 2nd-level indexing is finished
		bool enabled = enabledOption ?? devMode;

		return enabled
			? (true, "")
			: (false,
				$"To enable Second Level Indexing Set '{KurrentConfigurationKeys.Prefix}:SecondLevelIndexing:Enabled' to 'true'");
	}
}
