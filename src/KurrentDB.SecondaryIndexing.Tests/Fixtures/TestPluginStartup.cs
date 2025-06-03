// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using EventStore.Plugins.Licensing;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.Core.TransactionLog.Chunks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.SecondaryIndexing.Tests.Fixtures;

internal static class TestPluginStartup {
	public static WebApplication Configure(SecondaryIndexingPlugin plugin,
		IConfigurationBuilder? configurationBuilder = null) {
		var config = (configurationBuilder ?? new ConfigurationBuilder()).Build();

		var builder = WebApplication.CreateBuilder();

		builder.Services.AddSingleton<ILicenseService>(new Plugins.TestHelpers.Fixtures.FakeLicenseService())
			.AddSingleton(new TFChunkDbConfig("mem", 10000, 0,  new InMemoryCheckpoint(-1), new InMemoryCheckpoint(-1),
				new InMemoryCheckpoint(-1), new InMemoryCheckpoint(-1), new InMemoryCheckpoint(-1),
				new InMemoryCheckpoint(-1), new InMemoryCheckpoint(-1), new InMemoryCheckpoint(-1), true))
			.AddSingleton<IReadIndex<string>>(_ => null!);

		((IPlugableComponent)plugin).ConfigureServices(
			builder.Services,
			config);

		var app = builder.Build();
		((IPlugableComponent)plugin).ConfigureApplication(app, config);

		return app;
	}
}
