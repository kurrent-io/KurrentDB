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