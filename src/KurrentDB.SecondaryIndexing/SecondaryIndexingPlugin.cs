// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using EventStore.Plugins;
using EventStore.Plugins.Diagnostics;
using Kurrent.Surge.Schema;
using KurrentDB.Common.Configuration;
using KurrentDB.Core;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Core.Services.Storage;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.DuckDB;
using KurrentDB.Protocol.V2.CustomIndexes;
using KurrentDB.SecondaryIndexing.Diagnostics;
using KurrentDB.SecondaryIndexing.Indexes;
using KurrentDB.SecondaryIndexing.Indexes.Category;
using KurrentDB.SecondaryIndexing.Indexes.Custom;
using KurrentDB.SecondaryIndexing.Indexes.Custom.Management;
using KurrentDB.SecondaryIndexing.Indexes.Default;
using KurrentDB.SecondaryIndexing.Indexes.EventType;
using KurrentDB.SecondaryIndexing.Stats;
using KurrentDB.SecondaryIndexing.Storage;
using KurrentDB.SecondaryIndexing.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.SecondaryIndexing;

public sealed class SecondaryIndexingPluginOptions {
	public int CommitBatchSize { get; set; } = 50_000;
	public string? DbPath { get; set; }
}

public static class SecondaryIndexingConstants {
	public const string MeterName = "KurrentDB.SecondaryIndexes";
	public const string InjectionKey = "secondary-index";
}

public class SecondaryIndexingPlugin(SecondaryIndexReaders secondaryIndexReaders)
	: SubsystemsPlugin(name: PluginNames.SecondaryIndexes) {
	[Experimental("SECONDARY_INDEX")]
	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		var options = configuration
			.GetSection($"{KurrentConfigurationKeys.Prefix}:SecondaryIndexing:Options")
			.Get<SecondaryIndexingPluginOptions>() ?? new();
		services.AddSingleton(options);

		services.AddCommandService<CustomIndexCommandService, CustomIndexState>();
		services.AddSingleton<CustomIndexStreamNameMap>();
		services.AddSingleton<CustomIndexReadsideService>();
		services.AddSingleton<CustomIndexManager>();
		services.AddDuckDBSetup<IndexingDbSchema>();
		services.AddDuckDBSetup<InFlightSetup>();

		services.AddHostedService<DefaultIndexBuilder>();
		services.AddHostedService(sp => sp.GetRequiredService<CustomIndexManager>());

		services.AddSingleton<DefaultIndexInFlightRecords>();

		var meter = new Meter(SecondaryIndexingConstants.MeterName, "1.0.0");

		services.AddKeyedSingleton(SecondaryIndexingConstants.InjectionKey, meter);
		services.AddSingleton<ISecondaryIndexProcessor>(sp => sp.GetRequiredService<DefaultIndexProcessor>());
		services.AddSingleton<DefaultIndexProcessor>();

		services.AddSingleton<ISecondaryIndexReader, DefaultIndexReader>();
		services.AddSingleton<ISecondaryIndexReader, CategoryIndexReader>();
		services.AddSingleton<ISecondaryIndexReader, EventTypeIndexReader>();
		services.AddSingleton<ISecondaryIndexReader>(sp => sp.GetRequiredService<CustomIndexManager>());

		services.AddSingleton<StatsService>();
		services.AddHostedService(sp => new DbStatsTelemetryService(
			sp.GetRequiredService<StatsService>(),
			telemetry => PublishDiagnosticsData(telemetry, PluginDiagnosticsDataCollectionMode.Snapshot))
		);
		services.AddSingleton<GetLastPosition>(sp => sp.GetRequiredService<TFChunkDbConfig>().WriterCheckpoint.Read);

		// register into the inmemory schema registry
		services.AddStartupTask(services => new RegisterCustomIndexEvents(services));
	}

	public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
		base.ConfigureApplication(app, configuration);

		var indexReaders = app.ApplicationServices.GetServices<ISecondaryIndexReader>();

		secondaryIndexReaders.AddReaders(indexReaders.ToArray());
	}

	public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
		var enabledOption = configuration.GetValue<bool?>($"{KurrentConfigurationKeys.Prefix}:SecondaryIndexing:Enabled");
		bool enabled = enabledOption ?? true;

		return enabled
			? (true, "")
			: (false, $"To enable Second Level Indexing Set '{KurrentConfigurationKeys.Prefix}:SecondaryIndexing:Enabled' to 'true'");
	}
}

public class RegisterCustomIndexEvents(IServiceProvider services) : IClusterVNodeStartupTask {
	public async ValueTask Run(CancellationToken ct) {
		await RegisterType<CustomIndexCreated>(ct);
		await RegisterType<CustomIndexStarted>(ct);
		await RegisterType<CustomIndexStopped>(ct);
		await RegisterType<CustomIndexDeleted>(ct);

		ValueTask<RegisteredSchema> RegisterType<T>(CancellationToken ct) => services
			.GetRequiredService<ISchemaRegistry>()
			.RegisterSchema<T>(
				new SchemaInfo(SchemaName: $"${typeof(T).Name}", SchemaDataFormat.Json),
				cancellationToken: ct);
	}
}

