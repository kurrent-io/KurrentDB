using EventStore.Connect;
using EventStore.Connectors.Control;
using EventStore.Connectors.Management;
using EventStore.Connectors.System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static System.String;

namespace EventStore.Plugins.Connectors;

[UsedImplicitly]
public class ConnectorsPlugin : SubsystemsPlugin {
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
        services
            .AddNodeSystemInfoProvider()
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

        var instructions = "Please check the documentation for instructions on how to enable the plugin.";

        if (!enabled)
            return (enabled, instructions);

        var dataProtectionToken = configuration.GetValue(
            "KurrentDB:Connectors:DataProtection:Token",
            configuration.GetValue("KurrentDB:DataProtection:Token",
                configuration.GetValue("EventStore:Connectors:DataProtection:Token",
                    configuration.GetValue("EventStore:DataProtection:Token", ""))));

        if (IsNullOrWhiteSpace(dataProtectionToken)) {
            enabled      = false;
            instructions = $"Data protection token not found! {instructions}";
        }

        return (enabled, instructions);
    }
}