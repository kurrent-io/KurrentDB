// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Configuration.Sources;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EventStore.AutoScavenge.Tests;

public class DummyWebApplicationFactory : WebApplicationFactory<DummyStartup>, IAsyncLifetime {
	protected override void ConfigureWebHost(IWebHostBuilder builder) {
		builder.UseContentRoot(Directory.GetCurrentDirectory());
		builder.ConfigureAppConfiguration((_, config) => {
			config.AddInMemoryCollection(new Dictionary<string, string?> {
				{ $"{KurrentConfigurationKeys.Prefix}:AutoScavenge:Enabled", "true" },
				{ $"{KurrentConfigurationKeys.Prefix}:ClusterSize", "1" },
				{ $"{KurrentConfigurationKeys.Prefix}:Insecure", "true" },
			});
		});
	}

	protected override IHostBuilder CreateHostBuilder() {
		return Host.CreateDefaultBuilder()
			.ConfigureWebHostDefaults(builder => {
				builder.UseStartup<DummyStartup>();
			});
	}

	public async Task InitializeAsync() {
		await Services.GetRequiredService<AutoScavengePlugin>().Start();
	}

	async Task IAsyncLifetime.DisposeAsync() {
		await Services.GetRequiredService<AutoScavengePlugin>().Stop();
	}
}
