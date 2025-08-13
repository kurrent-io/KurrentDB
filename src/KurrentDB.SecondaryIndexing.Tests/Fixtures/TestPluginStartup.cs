// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using EventStore.Plugins.Licensing;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Index.Hashes;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.POC.IO.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.SecondaryIndexing.Tests.Fixtures;

internal static class TestPluginStartup {
	public static WebApplication Configure(SecondaryIndexingPlugin plugin,
		IConfigurationBuilder? configurationBuilder = null) {
		var config = (configurationBuilder ?? new ConfigurationBuilder()).Build();

		var builder = WebApplication.CreateBuilder();

		// TODO: delete this directory after use
		var dbPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
		Directory.CreateDirectory(dbPath);

		builder.Services.AddSingleton<ILicenseService>(new Plugins.TestHelpers.Fixtures.FakeLicenseService())
			.AddSingleton(new TFChunkDbConfig(dbPath, 10000, 0,  new InMemoryCheckpoint(-1), new InMemoryCheckpoint(-1),
				new InMemoryCheckpoint(-1), new InMemoryCheckpoint(-1), new InMemoryCheckpoint(-1),
				new InMemoryCheckpoint(-1), new InMemoryCheckpoint(-1), new InMemoryCheckpoint(-1), true))
			.AddSingleton<IReadIndex<string>>(_ => null!)
			.AddSingleton<IPublisher>(_ => null!)
			.AddSingleton<ISubscriber>(_ => null!)
			.AddSingleton<IClient>(_ => null!)
			.AddSingleton<IIndexBackend<string>>(_ => null!)
			.AddSingleton<ILongHasher<string>>(_ => null!);

		((IPlugableComponent)plugin).ConfigureServices(
			builder.Services,
			config);

		var app = builder.Build();
		((IPlugableComponent)plugin).ConfigureApplication(app, config);

		return app;
	}
}
