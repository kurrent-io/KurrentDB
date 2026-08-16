// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using Kurrent.Kontext.Modules.Memory;
using KurrentDB.Common.Configuration;
using KurrentDB.Core.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Plugins.Kontext;

[UsedImplicitly]
public class KontextPlugin() : SubsystemsPlugin(name: PluginNames.Kontext, requiredEntitlements: ["KONTEXT"]) {
	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) =>
		services.AddKontext(configuration);

	public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) => 
        app.UseKontext();

    public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
        var enabled = configuration.GetValue(
            $"KurrentDB:{Name}:Enabled",
            configuration.GetValue($"{Name}:Enabled",
                configuration.GetValue("Enabled", true)
            )
        );
        
        return (enabled, "Please check the documentation for instructions on how to enable the plugin.");
        
		// var enabled = configuration.GetValue($"{KurrentConfigurationKeys.Prefix}:Kontext:Enabled", false);
		// return (enabled, "Set KurrentDB__Kontext__Enabled to true to enable the kontext plugin.");
	}
}
