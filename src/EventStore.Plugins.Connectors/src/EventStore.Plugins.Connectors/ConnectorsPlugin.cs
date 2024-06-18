using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using EventStore.Connectors.Control;
using EventStore.Connectors.Control.Coordination;
using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Core.Services;
using EventStore.Core.Services.Storage.InMemory;
using EventStore.Plugins;
using EventStore.Streaming.Connectors;
using EventStore.Streaming.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using static System.StringComparison;

namespace EventStore.Connectors;

public readonly record struct TinyAppBuildContext(
    IServiceProvider HostServices,
    IConfiguration Configuration,
    ILogger Logger,
    WebApplicationBuilder AppBuilder
);

public abstract class TinyAppPlugin : SubsystemsPlugin, IAsyncDisposable {
    ILogger Logger   { get; set; }
    bool    Started  { get; set; }
    bool    Stopped  { get; set; }
    bool    Disposed { get; set; }

    WebApplication Application { get; set; }

    public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
        var enabled = configuration.GetValue(
            $"EventStore:Plugins:{Name}:Enabled",
            configuration.GetValue(
                $"{Name}:Enabled",
                configuration.GetValue(
                    "Enabled",
                    true // default to enabled for now at least
                )
            )
        );

        return (enabled, "Please check the documentation for instruction on out to enable the plugin.");
    }

    public override void ConfigureApplication(IApplicationBuilder esdb, IConfiguration configuration) {
        Logger = esdb.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(Name);

        // TODO SS: figure out what more host deps need to be registered for the plugin, thinking of diagnostics, etc.

        // var hostConfiguration = esdb.ApplicationServices.GetRequiredService<IConfiguration>();

        var hostConfiguration = configuration;

        // remove Kestrel configuration entries from the esdb host configuration
        // so that it doesn't interfere with the plugin host configuration

        var kestrelExcludeKeys = new[] {
            "KESTREL",
            "HTTP_PORTS",
            "HTTPS_PORTS",
            "ASPNETCORE_HTTP_PORTS",
            "ASPNETCORE_HTTPS_PORTS",
            "DOTNET_HTTP_PORTS",
            "DOTNET_HTTPS_PORTS",
            "ASPNETCORE_URLS"
        };

        var appConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                hostConfiguration.AsEnumerable()
                    .Where(entry => kestrelExcludeKeys.All(prefix => !entry.Key.StartsWith(prefix, OrdinalIgnoreCase)))
                    .ToDictionary(x => x.Key, kvp => kvp.Value))
            .Build();

        var appOptions = new WebApplicationOptions {
            ApplicationName = $"{CommandLineName}-plugin-app"
        };

        var builder = WebApplication.CreateSlimBuilder(appOptions);
        //
        // // this blows up because affects the HOST configuration?!?
        // // builder.WebHost.UseConfiguration(appConfiguration);

        // this needs to be automatically configured by the plugin based on available ports by default
        // it could be overriden, but should be able to work out of the box (multiple apps running in different ports)
        builder.WebHost.ConfigureKestrel(kestrel => {
            kestrel.ListenAnyIP(20000, options => options.Protocols = HttpProtocols.Http1);
            // kestrel.ListenAnyIP(21000, options => {
            //     options.Protocols = HttpProtocols.Http1AndHttp2;
            //     options.UseHttps();
            // });
        });

        // // Logger.LogWarning("[{PluginName}] Plugin {Protocols} endpoint available without TLS on port {Port}", Name, HttpProtocols.Http1, 20000);
        // // Logger.LogWarning("[{PluginName}] Plugin {Protocols} endpoint available with TLS on port {Port}", Name, HttpProtocols.Http1AndHttp2, 21000);
        //
        // //builder.Services.AddSingleton(appConfiguration); // TODO SS: do I need to register config again after configuring the web host?

        builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<ILoggerFactory>());
        builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<TimeProvider>());

        builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<IPublisher>());
        builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<ISubscriber>());
        builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<StandardComponents>()); // not sure... looks goofy

        var ctx = new TinyAppBuildContext(
            esdb.ApplicationServices,
            configuration,
            Logger,
            builder
        );

        Application = BuildApp(ctx);

        //Logger.LogWarning("[{PluginName}] Plugin application built", Name);

        // ----------------------------------------------
        // if the plugin is a subsystem plugin, it should be started and stopped with the host
        // but just in case, we can go rogue and start/stop it here
        // ----------------------------------------------
        var lifetime = esdb.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();

        lifetime.ApplicationStarted.Register(() => {
            _ = Application.StartAsync().ContinueWith(t => {
                if (!t.IsFaulted) {
                    Logger.LogWarning("[{PluginName}] Plugin application started", Name);
                    Started = true;
                    Stopped = false;
                }
                else
                    Logger.LogError(t.Exception, "[{PluginName}] Plugin application failed to start", Name);
            });
        });

        lifetime.ApplicationStopping.Register(() => {
            try {
                var publisher = Application.Services.GetRequiredService<IPublisher>();

                Application.StopAsync().GetAwaiter().GetResult();
                Logger.LogInformation("[{PluginName}] Plugin application stopped", Name);

                var tmp = publisher.ReadStreamLastEvent(SystemStreams.GossipStream).GetAwaiter().GetResult();
                var evt = JsonSerializer.Deserialize<JsonNode>(tmp!.Value.Event.Data.Span);
                Logger.LogWarning("[{PluginName}] Last gossip event: {Event}", Name, evt!.ToJsonString());
            }
            catch (Exception ex) {
                Logger.LogWarning(ex, "[{PluginName}] Plugin application stopped violently", Name);
            }
            finally {
                Started = false;
                Stopped = true;
            }
        });
    }

    // public override Task Start() {
    //     if (Disposed) {
    //         Logger.LogWarning("[{PluginName}] Plugin application already disposed", Name);
    //         return Task.CompletedTask;
    //     }
    //
    //     if (Started) {
    //         Logger.LogWarning("[{PluginName}] Plugin application already started", Name);
    //         return Task.CompletedTask;
    //     }
    //
    //     _ = Application.RunAsync();
    //
    //     // _ = Application.StartAsync().ContinueWith(t => {
    //     //     if (!t.IsFaulted) {
    //     //         Logger.LogInformation("[{PluginName}] Plugin application started", Name);
    //     //         Started = true;
    //     //         Stopped = false;
    //     //     }
    //     //     else
    //     //         Logger.LogError(t.Exception, "[{PluginName}] Plugin application failed to start", Name);
    //     // });
    //
    //     return Task.CompletedTask;
    // }

    // public override async Task Stop() {
    //     var tmp = Application.Services.GetRequiredService<IPublisher>().ReadStreamLastEvent(SystemStreams.GossipStream).GetAwaiter().GetResult();
    //     var evt = JsonSerializer.Deserialize<JsonNode>(tmp!.Value.Event.Data.Span);
    //     Logger.LogWarning("[{PluginName}] Last gossip event: {@Event}", Name, evt!.ToString());
    //
    //     if (Disposed) {
    //         Logger.LogWarning("[{PluginName}] Plugin application already disposed", Name);
    //         return;
    //     }
    //
    //     if (Stopped) {
    //         Logger.LogWarning("[{PluginName}] Plugin application already stopped", Name);
    //         return;
    //     }
    //
    //     try {
    //         await Application.StopAsync();
    //         Logger.LogInformation("[{PluginName}] Plugin application stopped", Name);
    //     }
    //     catch (Exception ex) {
    //         Logger.LogWarning(ex, "[{PluginName}] Plugin application stopped violently", Name);
    //     }
    //     finally {
    //         Started = false;
    //         Stopped = true;
    //     }
    // }

    protected virtual async ValueTask DisposeAsyncCore() {
        await Application.DisposeAsync();
    }

    public async ValueTask DisposeAsync() {
        Disposed = true;
        Started  = false;
        Stopped  = true;
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    public abstract WebApplication BuildApp(TinyAppBuildContext buildContext);
}

public class ConnectorsPlugin : TinyAppPlugin {
    public override WebApplication BuildApp(TinyAppBuildContext ctx) {
        // because (unfortunately) the gossip listener service raises a schemaless event,
        // a proper schema must be registered for it to be consumed by the control plane
        SchemaRegistry.Global.RegisterSchema<GossipUpdatedInMemory>(GossipListenerService.EventType, SchemaDefinitionType.Json)
            .AsTask().GetAwaiter().GetResult();

        ctx.AppBuilder.Services.AddSingleton(SchemaRegistry.Global);

        ctx.AppBuilder.Services.AddSingleton<INodeLifetimeService, NodeLifetimeService>();

        ctx.AppBuilder.Services.AddConnectorsControlPlane();

        var app = ctx.AppBuilder.Build();

        var summaries = new[] {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        app.MapGet(
            "/weatherforecast",
            () => {
                var forecast = Enumerable.Range(1, 5).Select(
                        index =>
                            new WeatherForecast(
                                DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                                Random.Shared.Next(-20, 55),
                                summaries[Random.Shared.Next(summaries.Length)]
                            )
                    )
                    .ToArray();

                return forecast;
            }
        );

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