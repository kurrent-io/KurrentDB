// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using EventStore.Plugins;
using EventStore.Plugins.Subsystems;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.SecondaryIndexing.Builders;
using KurrentDB.SecondaryIndexing.Indices;
using KurrentDB.SecondaryIndexing.Indices.Default;
using KurrentDB.SecondaryIndexing.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.SecondaryIndexing;

public interface ISecondaryIndexingPlugin : ISubsystemsPlugin;

public sealed class SecondaryIndexingPluginOptions {
	public int? CheckpointCommitBatchSize { get; set; }
	public uint? CheckpointCommitDelayMs { get; set; }
}

public static class SecondaryIndexingPluginFactory {
	public static ISecondaryIndexingPlugin Create<TStreamId>(VirtualStreamReader virtualStreamReader) =>
		new SecondaryIndexingPlugin<TStreamId>(virtualStreamReader);
}

internal class SecondaryIndexingPlugin<TStreamId>(VirtualStreamReader virtualStreamReader)
	: SubsystemsPlugin(name: "secondary-indexing"), ISecondaryIndexingPlugin {
	[Experimental("SECONDARY_INDEX")]
	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		services.AddSingleton<DuckDbDataSource>()
			.AddSingleton<ISecondaryIndex>(sp =>
				new DefaultIndex<TStreamId>(
					sp.GetRequiredService<DuckDbDataSource>(),
					sp.GetRequiredService<IReadIndex<TStreamId>>()
				)
			);

		var options = configuration.GetSection($"{KurrentConfigurationKeys.Prefix}:SecondaryIndexing:Options")
			.Get<SecondaryIndexingPluginOptions>();

		services.AddHostedService(sp =>
			new SecondaryIndexBuilder(
				sp.GetRequiredService<ISecondaryIndex>(),
				sp.GetRequiredService<IPublisher>(),
				sp.GetRequiredService<ISubscriber>(),
				options
			)
		);
	}

	public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
		base.ConfigureApplication(app, configuration);

		var index = app.ApplicationServices.GetService<ISecondaryIndex>();

		if (index != null)
			virtualStreamReader.Register(index.Readers.ToArray());
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
