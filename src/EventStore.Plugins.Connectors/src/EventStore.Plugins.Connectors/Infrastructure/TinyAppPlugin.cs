using EventStore.Core;
using EventStore.Core.Bus;
using EventStore.Plugins;
using EventStore.Plugins.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ReSharper disable once CheckNamespace
namespace EventStore.Connectors;

public readonly record struct TinyAppBuildContext(
    WebApplicationBuilder AppBuilder,
    IServiceProvider HostServices,
    IConfiguration Configuration,
    ILogger Logger
);

public abstract class TinyAppPlugin : SubsystemsPlugin, IAsyncDisposable {
    ILogger Logger   { get; set; }
    bool    Started  { get; set; }
    bool    Stopped  { get; set; }
    bool    Disposed { get; set; }

    IServiceCollection       HostServiceCollection   { get; set; }
    WebApplication           Application             { get; set; }
    IHostApplicationLifetime HostApplicationLifetime { get; set; }

    public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
        var enabled = configuration.GetValue(
            $"EventStore:Plugins:{Name}:Enabled",
            configuration.GetValue(
                $"{Name}:Enabled",
                configuration.GetValue("Enabled", true)
            )
        );

        return (enabled, "Please check the documentation for instructions on how to enable the plugin.");
    }

    public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
        HostServiceCollection = services;

        HostServiceCollection.Configure<HostOptions>(options => {
            options.ServicesStartConcurrently          = true;
            options.ServicesStopConcurrently           = true;
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
        });
    }

    public override void ConfigureApplication(IApplicationBuilder esdb, IConfiguration configuration) {
        Logger                  = esdb.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(GetType().Name);
        HostApplicationLifetime = esdb.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();

        // TODO SS: figure out what more host deps need to be registered for the plugin, thinking of diagnostics, etc.

        // var hostConfiguration = esdb.ApplicationServices.GetRequiredService<IConfiguration>();

        var hostConfiguration = configuration;

        // remove Kestrel configuration entries from the esdb host configuration
        // so that it doesn't interfere with the plugin host configuration

        // var kestrelExcludeKeys = new[] {
        //     "KESTREL",
        //     "HTTP_PORTS",
        //     "HTTPS_PORTS",
        //     "ASPNETCORE_HTTP_PORTS",
        //     "ASPNETCORE_HTTPS_PORTS",
        //     "DOTNET_HTTP_PORTS",
        //     "DOTNET_HTTPS_PORTS",
        //     "ASPNETCORE_URLS"
        // };
        //
        // var appConfiguration = new ConfigurationBuilder()
        //     .AddInMemoryCollection(
        //         hostConfiguration.AsEnumerable()
        //             .Where(entry => kestrelExcludeKeys.All(prefix => !entry.Key.StartsWith(prefix, OrdinalIgnoreCase)))
        //             .ToDictionary(x => x.Key, kvp => kvp.Value))
        //     .Build();

        // var appOptions = new WebApplicationOptions {
        //     ApplicationName = $"{CommandLineName}-plugin-app"
        // };

        // var builder = WebApplication.CreateSlimBuilder(appOptions);

        var builder = WebApplication.CreateBuilder();

        // // // this blows up because affects the HOST configuration...
        // // // builder.WebHost.UseConfiguration(appConfiguration);
        //
        // // this needs to be automatically configured by the plugin based on available ports by default
        // // it could be overriden, but should be able to work out of the box (multiple apps running in different ports)
        // builder.WebHost.ConfigureKestrel(kestrel => {
        //     kestrel.ListenAnyIP(20000, options => options.Protocols = HttpProtocols.Http1);
        //     // kestrel.ListenAnyIP(21000, options => {
        //     //     options.Protocols = HttpProtocols.Http1AndHttp2;
        //     //     options.UseHttps();
        //     // });
        // });

        // // Logger.LogWarning("[{PluginName}] Plugin {Protocols} endpoint available without TLS on port {Port}", Name, HttpProtocols.Http1, 20000);
        // // Logger.LogWarning("[{PluginName}] Plugin {Protocols} endpoint available with TLS on port {Port}", Name, HttpProtocols.Http1AndHttp2, 21000);
        //
        // //builder.Services.AddSingleton(appConfiguration); // TODO SS: do I need to register config again after configuring the web host?

        builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<ILoggerFactory>());
        builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<TimeProvider>());
        builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<IPublisher>());
        builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<ISubscriber>());
        builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<StandardComponents>());
        builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<IAuthorizationProvider>());

        var ctx = new TinyAppBuildContext(builder, esdb.ApplicationServices, configuration, Logger);

        Application = BuildApp(ctx);
    }

    public override Task Start() {
        if (Disposed) {
            Logger.LogWarning("{PluginName} {Version} plugin application already disposed", Name, Version);
            return Task.CompletedTask;
        }

        if (Started) {
            Logger.LogWarning("{PluginName} {Version} plugin application already started", Name, Version);
            return Task.CompletedTask;
        }

        return Application.StartAsync().ContinueWith(t => {
            if (t.IsFaulted) {
                Logger.LogCritical(t.Exception.Flatten(), "*** {PluginName} {Version} plugin application failed to start", Name, Version);
                HostApplicationLifetime.StopApplication();
            }
            else {
                Logger.LogInformation("{PluginName} {Version} plugin application started", Name, Version);
                Started = true;
                Stopped = false;
            }
        });
    }

    public override async Task Stop() {
        // if (Disposed) {
        //     Logger.LogWarning("[{PluginName}] Plugin application already disposed", Name);
        //     return;
        // }
        //
        // if (Stopped) {
        //     Logger.LogWarning("[{PluginName}] Plugin application already stopped", Name);
        //     return;
        // }

        try {
            await Application.StopAsync();
            Logger.LogInformation("{PluginName} {Version} plugin application stopped", Name, Version);
        }
        catch (Exception ex) {
            Logger.LogWarning(ex, "{PluginName} {Version} plugin application stopped violently", Name, Version);
        }
        finally {
            Started = false;
            Stopped = true;
        }
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

    protected abstract WebApplication BuildApp(TinyAppBuildContext buildContext);
}

// public abstract class TinyAppPlugin : SubsystemsPlugin, IAsyncDisposable {
//     ILogger Logger   { get; set; }
//     bool    Started  { get; set; }
//     bool    Stopped  { get; set; }
//     bool    Disposed { get; set; }
//
//     WebApplication           Application             { get; set; }
//     IHostApplicationLifetime HostApplicationLifetime { get; set; }
//
//     public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
//         var enabled = configuration.GetValue(
//             $"EventStore:Plugins:{Name}:Enabled",
//             configuration.GetValue(
//                 $"{Name}:Enabled",
//                 configuration.GetValue("Enabled", true)
//             )
//         );
//
//         return (enabled, "Please check the documentation for instructions on how to enable the plugin.");
//     }
//
//     IServiceCollection HostServiceCollection { get; set; }
//
//     public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
//         HostServiceCollection = services;
//     }
//
//     public override void ConfigureApplication(IApplicationBuilder esdb, IConfiguration configuration) {
//         Logger                  = esdb.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(Name);
//         HostApplicationLifetime = esdb.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
//
//         // TODO SS: figure out what more host deps need to be registered for the plugin, thinking of diagnostics, etc.
//
//         // var hostConfiguration = esdb.ApplicationServices.GetRequiredService<IConfiguration>();
//
//         var hostConfiguration = configuration;
//
//         // remove Kestrel configuration entries from the esdb host configuration
//         // so that it doesn't interfere with the plugin host configuration
//
//         // var kestrelExcludeKeys = new[] {
//         //     "KESTREL",
//         //     "HTTP_PORTS",
//         //     "HTTPS_PORTS",
//         //     "ASPNETCORE_HTTP_PORTS",
//         //     "ASPNETCORE_HTTPS_PORTS",
//         //     "DOTNET_HTTP_PORTS",
//         //     "DOTNET_HTTPS_PORTS",
//         //     "ASPNETCORE_URLS"
//         // };
//         //
//         // var appConfiguration = new ConfigurationBuilder()
//         //     .AddInMemoryCollection(
//         //         hostConfiguration.AsEnumerable()
//         //             .Where(entry => kestrelExcludeKeys.All(prefix => !entry.Key.StartsWith(prefix, OrdinalIgnoreCase)))
//         //             .ToDictionary(x => x.Key, kvp => kvp.Value))
//         //     .Build();
//
//         var appOptions = new WebApplicationOptions {
//             ApplicationName = $"{CommandLineName}-plugin-app"
//         };
//
//         var builder = WebApplication.CreateSlimBuilder(appOptions);
//
//         // // // this blows up because affects the HOST configuration...
//         // // // builder.WebHost.UseConfiguration(appConfiguration);
//         //
//         // // this needs to be automatically configured by the plugin based on available ports by default
//         // // it could be overriden, but should be able to work out of the box (multiple apps running in different ports)
//         // builder.WebHost.ConfigureKestrel(kestrel => {
//         //     kestrel.ListenAnyIP(20000, options => options.Protocols = HttpProtocols.Http1);
//         //     // kestrel.ListenAnyIP(21000, options => {
//         //     //     options.Protocols = HttpProtocols.Http1AndHttp2;
//         //     //     options.UseHttps();
//         //     // });
//         // });
//
//         // // Logger.LogWarning("[{PluginName}] Plugin {Protocols} endpoint available without TLS on port {Port}", Name, HttpProtocols.Http1, 20000);
//         // // Logger.LogWarning("[{PluginName}] Plugin {Protocols} endpoint available with TLS on port {Port}", Name, HttpProtocols.Http1AndHttp2, 21000);
//         //
//         // //builder.Services.AddSingleton(appConfiguration); // TODO SS: do I need to register config again after configuring the web host?
//
//         builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<ILoggerFactory>());
//         builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<TimeProvider>());
//         builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<IPublisher>());
//         builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<ISubscriber>());
//         builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<StandardComponents>());
//         builder.Services.AddSingleton(esdb.ApplicationServices.GetRequiredService<IAuthorizationProvider>());
//
//         HostServiceCollection.Configure<HostOptions>(options => {
//             options.ServicesStartConcurrently          = true;
//             options.ServicesStopConcurrently           = true;
//             options.ShutdownTimeout                    = TimeSpan.FromSeconds(15);
//             options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
//         });
//
//         var ctx = new TinyAppBuildContext(esdb.ApplicationServices, configuration, Logger, builder, HostServiceCollection);
//
//         Application = BuildApp(ctx);
//     }
//
//     public override Task Start() {
//         if (Disposed) {
//             Logger.LogWarning("[{PluginName}] Plugin application already disposed", Name);
//             return Task.CompletedTask;
//         }
//
//         if (Started) {
//             Logger.LogWarning("[{PluginName}] Plugin application already started", Name);
//             return Task.CompletedTask;
//         }
//
//         return Application.StartAsync().ContinueWith(t => {
//             if (t.IsFaulted) {
//                 Logger.LogCritical(t.Exception.Flatten(), "[{PluginName}] Plugin application failed to start. Requesting shutdown...", Name);
//                 HostApplicationLifetime.StopApplication();
//             }
//             else {
//                 Logger.LogInformation("[{PluginName}] Plugin application started", Name);
//                 Started = true;
//                 Stopped = false;
//             }
//         });
//     }
//
//     public override async Task Stop() {
//         // if (Disposed) {
//         //     Logger.LogWarning("[{PluginName}] Plugin application already disposed", Name);
//         //     return;
//         // }
//         //
//         // if (Stopped) {
//         //     Logger.LogWarning("[{PluginName}] Plugin application already stopped", Name);
//         //     return;
//         // }
//
//         try {
//             await Application.StopAsync();
//             Logger.LogInformation("[{PluginName}] Plugin application stopped", Name);
//         }
//         catch (Exception ex) {
//             Logger.LogWarning(ex, "[{PluginName}] Plugin application stopped violently", Name);
//         }
//         finally {
//             Started = false;
//             Stopped = true;
//         }
//     }
//
//     // public override Task Start() {
//     //     if (Disposed) {
//     //         Logger.LogWarning("[{PluginName}] Plugin application already disposed", Name);
//     //         return Task.CompletedTask;
//     //     }
//     //
//     //     if (Started) {
//     //         Logger.LogWarning("[{PluginName}] Plugin application already started", Name);
//     //         return Task.CompletedTask;
//     //     }
//     //
//     //     _ = Application.RunAsync();
//     //
//     //     // _ = Application.StartAsync().ContinueWith(t => {
//     //     //     if (!t.IsFaulted) {
//     //     //         Logger.LogInformation("[{PluginName}] Plugin application started", Name);
//     //     //         Started = true;
//     //     //         Stopped = false;
//     //     //     }
//     //     //     else
//     //     //         Logger.LogError(t.Exception, "[{PluginName}] Plugin application failed to start", Name);
//     //     // });
//     //
//     //     return Task.CompletedTask;
//     // }
//
//     // public override async Task Stop() {
//     //     var tmp = Application.Services.GetRequiredService<IPublisher>().ReadStreamLastEvent(SystemStreams.GossipStream).GetAwaiter().GetResult();
//     //     var evt = JsonSerializer.Deserialize<JsonNode>(tmp!.Value.Event.Data.Span);
//     //     Logger.LogWarning("[{PluginName}] Last gossip event: {@Event}", Name, evt!.ToString());
//     //
//     //     if (Disposed) {
//     //         Logger.LogWarning("[{PluginName}] Plugin application already disposed", Name);
//     //         return;
//     //     }
//     //
//     //     if (Stopped) {
//     //         Logger.LogWarning("[{PluginName}] Plugin application already stopped", Name);
//     //         return;
//     //     }
//     //
//     //     try {
//     //         await Application.StopAsync();
//     //         Logger.LogInformation("[{PluginName}] Plugin application stopped", Name);
//     //     }
//     //     catch (Exception ex) {
//     //         Logger.LogWarning(ex, "[{PluginName}] Plugin application stopped violently", Name);
//     //     }
//     //     finally {
//     //         Started = false;
//     //         Stopped = true;
//     //     }
//     // }
//
//     protected virtual async ValueTask DisposeAsyncCore() {
//         await Application.DisposeAsync();
//     }
//
//     public async ValueTask DisposeAsync() {
//         Disposed = true;
//         Started  = false;
//         Stopped  = true;
//         await DisposeAsyncCore();
//         GC.SuppressFinalize(this);
//     }
//
//     protected abstract WebApplication BuildApp(TinyAppBuildContext buildContext);
// }