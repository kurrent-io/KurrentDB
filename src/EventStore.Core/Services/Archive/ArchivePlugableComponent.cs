// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.Configuration.Sources;
using EventStore.Core.Resilience;
using EventStore.Core.Services.Archive.Archiver;
using EventStore.Core.Services.Archive.Naming;
using EventStore.Core.Services.Archive.Storage;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Plugins;
using EventStore.Plugins.Licensing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventStore.Core.Services.Archive;

public class ArchivePlugableComponent : IPlugableComponent {
	public string Name => "Archiver";

	public string DiagnosticsName => Name;

	public KeyValuePair<string, object>[] DiagnosticsTags => [];

	public string Version => "0.0.1";

	public bool Enabled { get; private set; }

	public string LicensePublicKey => LicenseConstants.LicensePublicKey;

	private readonly bool _isArchiver;

	public ArchivePlugableComponent(bool isArchiver) {
		_isArchiver = isArchiver;
	}

	public void ConfigureApplication(IApplicationBuilder builder, IConfiguration configuration) {
		if (!Enabled)
			return;

		_ = builder.ApplicationServices.GetService<ArchiverService>();

		var licenseService = builder.ApplicationServices.GetRequiredService<ILicenseService>();

		_ = LicenseMonitor.MonitorAsync(
			featureName: Name,
			requiredEntitlements: ["ARCHIVE"],
			licenseService: licenseService,
			onLicenseException: licenseService.RejectLicense,
			logger: builder.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(GetType()));
	}

	public void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		var options = configuration.GetSection($"{KurrentConfigurationKeys.Prefix}:Archive").Get<ArchiveOptions>() ?? new();
		Enabled = options.Enabled;

		if (!Enabled)
			return;

		services.AddSingleton<ArchiveOptions>(options);
		services.AddSingleton<IArchiveStorage>(s => {
			var resolver = s.GetRequiredService<IArchiveNamingStrategy>();
			return ArchiveStorageFactory.Create(options, resolver);
		});
		services.Decorate<IReadOnlyList<IClusterVNodeStartupTask>>(AddArchiveCatchupTask);
		services.AddSingleton<IArchiveNamingStrategy, ArchiveNamingStrategy>();

		if (_isArchiver) {
			services.AddSingleton<ArchiverService>(s => {
				var archiveStorage = s.GetRequiredService<IArchiveStorage>();
				return new(
					mainBus: s.GetRequiredService<ISubscriber>(),
					archiveStorage: new ResilientArchiveStorage(ResiliencePipelines.RetryForever, archiveStorage),
					chunkManager: s.GetRequiredService<IChunkRegistry<IChunkBlob>>());
			});
		}
	}

	private static IReadOnlyList<IClusterVNodeStartupTask> AddArchiveCatchupTask(
		IReadOnlyList<IClusterVNodeStartupTask> startupTasks,
		IServiceProvider serviceProvider) {

		var newStartupTasks = new List<IClusterVNodeStartupTask>();
		if (startupTasks != null)
			newStartupTasks.AddRange(startupTasks);

		var standardComponents = serviceProvider.GetRequiredService<StandardComponents>();
		newStartupTasks.Add(new ArchiveCatchup.ArchiveCatchup(
			dbPath: standardComponents.DbConfig.Path,
			writerCheckpoint: standardComponents.DbConfig.WriterCheckpoint,
			chaserCheckpoint: standardComponents.DbConfig.ChaserCheckpoint,
			epochCheckpoint: standardComponents.DbConfig.EpochCheckpoint,
			chunkSize: standardComponents.DbConfig.ChunkSize,
			new ResilientArchiveStorage(
				ResiliencePipelines.RetryForever,
				serviceProvider.GetRequiredService<IArchiveStorage>()),
			serviceProvider.GetRequiredService<IArchiveNamingStrategy>()));

		return newStartupTasks;
	}
}
