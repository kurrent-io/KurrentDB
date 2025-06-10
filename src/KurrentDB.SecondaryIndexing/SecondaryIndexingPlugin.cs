// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using EventStore.Plugins;
using EventStore.Plugins.Subsystems;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.SecondaryIndexing.Builders;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.SecondaryIndexing;

public interface ISecondaryIndexingPlugin : ISubsystemsPlugin;

public sealed class SecondaryIndexingPluginOptions {
	public int CommitBatchSize { get; set; } = 50_000;
}

public static class SecondaryIndexingPluginFactory {
	public static ISecondaryIndexingPlugin Create(VirtualStreamReader virtualStreamReader) =>
		new SecondaryIndexingPlugin(virtualStreamReader);
}

internal class SecondaryIndexingPlugin(VirtualStreamReader virtualStreamReader)
	: SubsystemsPlugin(name: "secondary-indexing"), ISecondaryIndexingPlugin {
	[Experimental("SECONDARY_INDEX")]
	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		var options = configuration
			.GetSection($"{KurrentConfigurationKeys.Prefix}:SecondaryIndexing:Options")
			.Get<SecondaryIndexingPluginOptions>() ?? new();

		services.AddSingleton<DuckDbDataSourceOptions>(sp => {
			var dbConfig = sp.GetRequiredService<TFChunkDbConfig>();
			var connectionString = $"Data Source={Path.Combine(dbConfig.Path, "index.db")};";

			return new DuckDbDataSourceOptions { ConnectionString = connectionString };
		});
		services.AddSingleton<DuckDbDataSource>();
		services.AddSingleton(sp =>
			new DefaultIndex(
				sp.GetRequiredService<DuckDbDataSource>(),
				sp.GetRequiredService<IReadIndex<string>>(),
				options.CommitBatchSize
			)
		);
		services.AddHostedService(sp =>
			new SecondaryIndexBuilder(
				sp.GetRequiredService<DefaultIndex>(),
				sp.GetRequiredService<IPublisher>(),
				sp.GetRequiredService<ISubscriber>(),
				options
			)
		);
	}

	public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
		base.ConfigureApplication(app, configuration);

		var index = app.ApplicationServices.GetService<DefaultIndex>();

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
