// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Connect;
using EventStore.Plugins;
using KurrentDB.Connectors.Infrastructure.System.Node.NodeSystemInfo;
using KurrentDB.Connectors.Planes.Control;
using KurrentDB.Connectors.Planes.Management;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Plugins.Connectors;

[UsedImplicitly]
public class ConnectorsPlugin : SubsystemsPlugin {
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
        services
            .AddNodeSystemInfoProvider()
            .AddSurgeSchemaRegistry()
            .AddSurgeSystemComponents()
            .AddSurgeDataProtection(configuration)
            .AddConnectorsControlPlane()
            .AddConnectorsManagementPlane();
    }

    public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
        app.UseConnectorsManagementPlane();
    }

    public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
        var enabled = configuration.GetValue(
            $"KurrentDB:{Name}:Enabled",
            configuration.GetValue($"{Name}:Enabled",
                configuration.GetValue("Enabled", true)
            )
        );

        return (enabled, "Please check the documentation for instructions on how to enable the plugin.");
    }
}
