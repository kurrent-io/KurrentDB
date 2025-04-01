// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Configuration.Sources;
using EventStore.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.TcpPlugin;

public class TcpApiPlugin : SubsystemsPlugin {
	public TcpApiPlugin() : base(requiredEntitlements: ["TCP_CLIENT_PLUGIN"]) {
	}

	public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
		var enabled = configuration.GetValue($"{KurrentConfigurationKeys.Prefix}:TcpPlugin:EnableExternalTcp", defaultValue: false);
		return (enabled, $"Set '{KurrentConfigurationKeys.Prefix}:TcpPlugin:EnableExternalTcp' to 'true' to enable");
	}

	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		var options = configuration.GetSection(KurrentConfigurationKeys.Prefix).Get<EventStoreOptions>() ?? new();
		services.AddSingleton(options);
		services.AddHostedService<PublicTcpApiService>();
	}
}
