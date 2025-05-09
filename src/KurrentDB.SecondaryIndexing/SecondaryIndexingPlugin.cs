// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using EventStore.Plugins;
using EventStore.Plugins.Subsystems;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Builders;
using KurrentDB.SecondaryIndexing.Indices;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace KurrentDB.SecondaryIndexing;

public class SecondaryIndexPluginConfigurationOptions<TStreamId> {
	public required IPublisher Publisher { get; set; }
	public required ISubscriber Subscriber { get; set; }
	public required IReadIndex<TStreamId> ReadIndex { get; set; }
}

public interface ISecondaryIndexingPlugin : ISubsystemsPlugin {
	IEnumerable<IVirtualStreamReader> IndicesVirtualStreamReaders { get; }

	void Configure<TStreamId>(SecondaryIndexPluginConfigurationOptions<TStreamId> configurationOptions);
}

public static class SecondaryIndexingPluginFactory {
	public static ISecondaryIndexingPlugin Create() =>
		new SecondaryIndexingPlugin();
}

internal class SecondaryIndexingPlugin(ISecondaryIndex? index = null)
	: SubsystemsPlugin(name: "secondary-indexing"), ISecondaryIndexingPlugin {
	private SecondaryIndexBuilder? _secondaryIndexBuilder;

	public IEnumerable<IVirtualStreamReader> IndicesVirtualStreamReaders =>
		_secondaryIndexBuilder?.IndexVirtualStreamReaders ?? [];

	public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {

		base.ConfigureApplication(app, configuration);
	}

	[Experimental("SECONDARYINDEXING")]
	public void Configure<TStreamId>(SecondaryIndexPluginConfigurationOptions<TStreamId> configurationOptions) {
		if (index == null) return;

		_secondaryIndexBuilder = new SecondaryIndexBuilder(index, configurationOptions.Publisher);

		configurationOptions.Subscriber.Subscribe<SystemMessage.SystemReady>(_secondaryIndexBuilder);
		configurationOptions.Subscriber.Subscribe<SystemMessage.BecomeShuttingDown>(_secondaryIndexBuilder);
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
