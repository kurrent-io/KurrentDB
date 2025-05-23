// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using EventStore.Plugins.Licensing;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Tests.TransactionLog;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Plugins.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.SecondaryIndexing.Tests;

internal static class TestPluginStartup {
	public static WebApplication Configure<TStreamId>(SecondaryIndexingPlugin<TStreamId> plugin,
		IConfigurationBuilder? configurationBuilder = null) {
		var config = (configurationBuilder ?? new ConfigurationBuilder()).Build();

		var builder = WebApplication.CreateBuilder();

		builder.Services.AddSingleton<ILicenseService>(new Fixtures.FakeLicenseService())
			.AddSingleton(new TFChunkDbConfig("mem", 10000, 0,  new InMemoryCheckpoint(-1), new InMemoryCheckpoint(-1),
				new InMemoryCheckpoint(-1), new InMemoryCheckpoint(-1), new InMemoryCheckpoint(-1),
				new InMemoryCheckpoint(-1), new InMemoryCheckpoint(-1), new InMemoryCheckpoint(-1), true))
			.AddSingleton<IReadIndex<TStreamId>>(_ => null!);

		((IPlugableComponent)plugin).ConfigureServices(
			builder.Services,
			config);

		var app = builder.Build();
		((IPlugableComponent)plugin).ConfigureApplication(app, config);

		return app;
	}
}
