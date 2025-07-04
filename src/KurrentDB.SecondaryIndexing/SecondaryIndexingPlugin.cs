// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using EventStore.Plugins;
using EventStore.Plugins.Subsystems;
using KurrentDB.Common.Configuration;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Services.Storage.InMemory;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.SecondaryIndexing.Builders;
using KurrentDB.SecondaryIndexing.Indexes;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Indexes.Diagnostics;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Indexes.Stream;
using KurrentDB.SecondaryIndexing.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.SecondaryIndexing;

public interface ISecondaryIndexingPlugin : ISubsystemsPlugin;

public sealed class SecondaryIndexingPluginOptions {
	public int CommitBatchSize { get; set; } = 50_000;
	public string? DbPath { get; set; }
}

public static class SecondaryIndexingPluginFactory {
	public static ISecondaryIndexingPlugin Create(VirtualStreamReader virtualStreamReader) =>
		new SecondaryIndexingPlugin(virtualStreamReader);
}

internal class SecondaryIndexingPlugin(VirtualStreamReader virtualStreamReader)
	: SubsystemsPlugin(name: "SecondaryIndexes"), ISecondaryIndexingPlugin {
	[Experimental("SECONDARY_INDEX")]
	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		var options = configuration
			.GetSection($"{KurrentConfigurationKeys.Prefix}:SecondaryIndexing:Options")
			.Get<SecondaryIndexingPluginOptions>() ?? new();
		services.AddSingleton(options);

		services.AddSingleton<DuckDbDataSourceOptions>(sp => {
			var dbPath = options.DbPath ?? Path.Combine(sp.GetRequiredService<TFChunkDbConfig>().Path, "index.db");

			return new DuckDbDataSourceOptions { ConnectionString = $"Data Source={dbPath};" };
		});
		services.AddSingleton<DuckDbDataSource>(sp => {
			var dbSource = new DuckDbDataSource(sp.GetRequiredService<DuckDbDataSourceOptions>());
			dbSource.InitDb();
			return dbSource;
		});
		services.AddHostedService<SecondaryIndexBuilder>();

		services.AddSingleton<DefaultIndexInFlightRecords>();
		services.AddSingleton<QueryInFlightRecords<EventTypeSql.EventTypeRecord>>(sp =>
			sp.GetRequiredService<DefaultIndexInFlightRecords>().QueryInFlightRecords
		);
		services.AddSingleton<QueryInFlightRecords<CategorySql.CategoryRecord>>(sp =>
			sp.GetRequiredService<DefaultIndexInFlightRecords>().QueryInFlightRecords
		);

		var conf = MetricsConfiguration.Get(configuration);
		var coreMeter = new Meter(conf.CoreMeterName, version: "1.0.0");

		services.AddSingleton<ISecondaryIndexProgressTracker>(sp =>
			new SecondaryIndexProgressTracker(
				coreMeter,
				"indexes.secondary"
			)
		);
		services.AddSingleton<ISecondaryIndexProcessor>(sp => sp.GetRequiredService<DefaultIndexProcessor>());
		services.AddSingleton<DefaultIndexProcessor>();
		services.AddSingleton<CategoryIndexProcessor>();
		services.AddSingleton<EventTypeIndexProcessor>();
		services.AddSingleton<StreamIndexProcessor>();

		services.AddSingleton<ISecondaryIndexReader, DefaultIndexReader>();
		services.AddSingleton<ISecondaryIndexReader, CategoryIndexReader>();
		services.AddSingleton<ISecondaryIndexReader, EventTypeIndexReader>();
	}

	public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
		base.ConfigureApplication(app, configuration);

		var indexReaders = app.ApplicationServices.GetServices<ISecondaryIndexReader>();

		virtualStreamReader.Register(indexReaders.ToArray<IVirtualStreamReader>());
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
