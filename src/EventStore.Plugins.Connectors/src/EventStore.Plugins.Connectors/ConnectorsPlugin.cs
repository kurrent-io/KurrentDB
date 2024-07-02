using EventStore.Connectors;
using EventStore.Connectors.Control;
using EventStore.Connectors.Control.Coordination;
using EventStore.Connectors.Management;
using EventStore.Core.Services.Storage.InMemory;
using EventStore.Streaming.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.Plugins.Connectors;

[UsedImplicitly]
public class ConnectorsPlugin : TinyAppPlugin {
    protected override WebApplication BuildApp(TinyAppBuildContext ctx) {
        // Because (unfortunately) the gossip listener service raises a schemaless event,
        // a proper schema must be registered for it to be consumed by the control plane
        SchemaRegistry.Global.RegisterSchema<GossipUpdatedInMemory>(
                GossipListenerService.EventType,
                SchemaDefinitionType.Json
            )
            .AsTask().GetAwaiter().GetResult();

        ctx.AppBuilder.Services.AddSingleton(SchemaRegistry.Global);
        ctx.AppBuilder.Services.AddSingleton<INodeLifetimeService, NodeLifetimeService>();

        ctx.AppBuilder.Services.AddConnectorsManagement();
        ctx.AppBuilder.Services.AddConnectorsControlPlane();

        var app = ctx.AppBuilder.Build();

        app.UseConnectorsManagement();

        return app;
    }
}

//
// sealed class ConnectorsOldPlugin : SubsystemsPlugin {
//     public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) => (true, "Really?!");
//
//     public override void ConfigureApplication(IApplicationBuilder esdb, IConfiguration configuration) {
//         // hardcore!!! AHAHHAAHHAHHAAHAHHA MUAH AH AH AH AH AH
//
//         var logger = esdb.ApplicationServices
//             .GetRequiredService<ILoggerFactory>()
//             .CreateLogger("ConnectorsPlugin");
//
//         logger.LogWarning("[CONNECTORS] Configuring application services...");
//
//         var builder = WebApplication.CreateSlimBuilder();
//
//         // because (unfortunately) the gossip listener service raises a schemaless event,
//         // a proper schema must be registered for it to be consumed by the control plane
//         SchemaRegistry.Global.RegisterSchema<GossipUpdatedInMemory>(GossipListenerService.EventType, SchemaDefinitionType.Json)
//             .AsTask().GetAwaiter().GetResult();
//
//         builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<ILoggerFactory>());
//         builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<TimeProvider>());
//
//         builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<IPublisher>());
//         builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<ISubscriber>());
//
//         builder.Services.AddSingleton(SchemaRegistry.Global);
//         builder.Services.AddSingleton<SystemClient>();
//         builder.Services.AddSingleton<SystemConnectorsFactory>();
//
//         logger.LogWarning("[CONNECTORS] Application services configured");
//
//         var application = builder.Build();
//
//         logger.LogWarning("[CONNECTORS] Configuring application...");
//
//         var summaries = new[] {
//             "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
//         };
//
//         application.MapGet(
//             "/weatherforecast",
//             () => {
//                 var forecast = Enumerable.Range(1, 5).Select(
//                         index =>
//                             new WeatherForecast(
//                                 DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
//                                 Random.Shared.Next(-20, 55),
//                                 summaries[Random.Shared.Next(summaries.Length)]
//                             )
//                     )
//                     .ToArray();
//
//                 return forecast;
//             }
//         );
//
//         var lifetime = esdb.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
//
//         lifetime.ApplicationStarted.Register(() => {
//             application.RunAsync("http://localhost:20000");
//             logger.LogWarning("[{PluginName}] Application starting...", nameof(Name));
//         });
//
//         lifetime.ApplicationStopping.Register(() => {
//             application.StopAsync().GetAwaiter().GetResult();
//             logger.LogWarning("[{PluginName}] Application stopped", nameof(Name));
//         });
//
//         logger.LogWarning("[{PluginName}] Application configured", nameof(Name));
//     }
// }

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary) {
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}