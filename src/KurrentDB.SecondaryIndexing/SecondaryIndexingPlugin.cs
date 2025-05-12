// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using EventStore.Plugins;
using EventStore.Plugins.Subsystems;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.SecondaryIndexing.Builders;
using KurrentDB.SecondaryIndexing.Indices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.SecondaryIndexing;

public interface ISecondaryIndexingPlugin : ISubsystemsPlugin {
	IEnumerable<IVirtualStreamReader> IndicesVirtualStreamReaders { get; }
}

public static class SecondaryIndexingPluginFactory {
	public static ISecondaryIndexingPlugin Create<TStreamId>() =>
		new SecondaryIndexingPlugin<TStreamId>();
}

internal class SecondaryIndexingPlugin<TStreamId>(ISecondaryIndex? index = null)
	: SubsystemsPlugin(name: "secondary-indexing"), ISecondaryIndexingPlugin {

	public IEnumerable<IVirtualStreamReader> IndicesVirtualStreamReaders =>
		index?.Readers ?? [];

	[Experimental("SECONDARYINDEXING")]
	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		if (index != null)
			services.AddHostedService(sp =>
				new SecondaryIndexBuilder(
					index,
					sp.GetRequiredService<IPublisher>(),
					sp.GetRequiredService<ISubscriber>()
				)
			);
	}

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
