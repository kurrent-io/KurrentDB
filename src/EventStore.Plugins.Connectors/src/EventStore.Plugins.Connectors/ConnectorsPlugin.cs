using EventStore.Connect;
using EventStore.Connectors.Control;
using EventStore.Connectors.Management;
using EventStore.Connectors.Management.Reactors;
using EventStore.Connectors.System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Plugins.Connectors;

[UsedImplicitly]
public class ConnectorsPlugin : SubsystemsPlugin {
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
        services
            .AddNodeSystemInfoProvider()
            .AddConnectSystemComponents()
            .AddConnectorsControlPlane()
            .AddConnectorsManagementPlane();
    }

    public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
        app.UseConnectorsManagementPlane();
    }

    public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
        var enabled = configuration.GetValue(
            $"EventStore:{Name}:Enabled",
            configuration.GetValue($"{Name}:Enabled",
                configuration.GetValue("Enabled", true)
            )
        );

        return (enabled, "Please check the documentation for instructions on how to enable the plugin.");
    }
}


public record ConnectorsPluginOptions {
    public ConnectorsStreamSupervisorOptions ConnectorsStreamSupervisor { get; set; }


    // await TryConfigureStream(ConnectorQueryConventions.Streams.ConnectorsStateProjectionStream, maxCount: 10);
    // await TryConfigureStream(ConnectorQueryConventions.Streams.ConnectorsStateProjectionCheckpointsStream, maxCount: 10);
}