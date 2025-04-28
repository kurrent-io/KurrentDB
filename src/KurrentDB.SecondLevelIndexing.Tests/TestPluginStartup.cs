// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using EventStore.Plugins.Licensing;
using KurrentDB.Plugins.TestHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.SecondLevelIndexing.Tests;

public static class TestPluginStartup {
	public static void Configure(SecondLevelIndexingPlugin plugin, IConfigurationBuilder? configurationBuilder = null) {
		var config = (configurationBuilder ?? new ConfigurationBuilder()).Build();

		var builder = WebApplication.CreateBuilder();

		builder.Services.AddSingleton<ILicenseService>(new Fixtures.FakeLicenseService());

		((IPlugableComponent)plugin).ConfigureServices(
			builder.Services,
			config);

		var app = builder.Build();
		((IPlugableComponent)plugin).ConfigureApplication(app, config);
	}
}
