using EventStore.Connect;
using EventStore.Connect.Connectors;
using EventStore.Connectors;
using EventStore.Connectors.Control;
using EventStore.Connectors.Management;
using EventStore.Connectors.System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EventStore.Plugins.Connectors;

[UsedImplicitly]
public class ConnectorsPlugin : SubsystemsPlugin {
    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
        services.Configure<HostOptions>(options => {
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
        });

        services
            .AddNodeSystemInfoProvider()
            .AddConnectSystemComponents()
            .AddConnectorsManagementPlane()
            .AddConnectorsControlPlane();
    }

    public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
        // var factory = new SystemConnectorsFactory(new SystemConnectorsFactoryOptions(), new SystemConnectorsValidation(), app.ApplicationServices);
        //
        // var connector = factory.CreateConnector(Guid.NewGuid(), new Dictionary<string, string?> {
        //     ["InstanceTypeName"] = "EventStore.Connectors.Kafka.KafkaSink",
        //     ["AutoLock:Enabled"] = "false"
        // });

        app.UseConnectorsManagementPlane();
    }

    public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
        var enabled = configuration.GetValue(
            $"EventStore:Plugins:{Name}:Enabled",
            configuration.GetValue($"{Name}:Enabled",
                configuration.GetValue("Enabled", true)
            )
        );

        return (enabled, "Please check the documentation for instructions on how to enable the plugin.");
    }
}

// [UsedImplicitly]
// public class ConnectorsPlugin : TinyAppPlugin {
//     protected override WebApplication BuildApp(TinyAppBuildContext buildContext) {
//         buildContext.AppBuilder.Services
//             .AddNodeSystemInfoProvider()
//             .AddConnectSystemComponents()
//             .AddConnectorsManagementPlane()
//             .AddConnectorsControlPlane();
//
//         var app = buildContext.AppBuilder.Build();
//
//         app.UseConnectorsManagementPlane();
//
//         return app;
//     }
// }