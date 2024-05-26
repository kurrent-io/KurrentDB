// using EventStore.Plugins.Telemetry.RealPlugins;
// using Microsoft.Extensions.DependencyInjection;
// using OpenTelemetry;
// using OpenTelemetry.Metrics;
// using OpenTelemetry.Resources;
//
// var meterProviderBuilder = Sdk.CreateMeterProviderBuilder();
//
// var plugins = new List<IMeteredSubsystemsPlugin> {
//    (IMeteredSubsystemsPlugin) Activator.CreateInstance(typeof(Plugin0))!, 
//    (IMeteredSubsystemsPlugin) Activator.CreateInstance(typeof(Plugin1))!
// };
//
// foreach (var plugin in plugins) 
//     meterProviderBuilder.AddMeter(plugin.MeterName);
//
// using var meterProvider = meterProviderBuilder
//     .ConfigureResource(x => x.AddService("TelemetryMagic"))
//     .AddConsoleExporter()
//     .Build();
//
// var services = new ServiceCollection();
//
// services.AddOpenTelemetry().WithMetrics();
//
// foreach (dynamic plugin in plugins) 
//     plugin.JustTriggerCounter();

using EventStore.Plugins.Telemetry.RealPlugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(
    kestrel => kestrel.ListenAnyIP(10000, o => o.Protocols = HttpProtocols.Http1)
);

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(x => x.AddService("TelemetryMagic"))
    .WithMetrics(metrics => metrics
        // .AddAspNetCoreInstrumentation()
        .AddMeter("Plugin0")
        .AddPrometheusExporter()
        // .AddConsoleExporter((options, reader) => {
        //     reader.TemporalityPreference = MetricReaderTemporalityPreference.Cumulative;
        //     // reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 1000;
        // })
    );

builder.Services.AddSingleton<Plugin0>();


var app = builder.Build();

app.MapGet("/plugin", ([FromServices] Plugin0 plugin) => plugin.CountRequest());
app.UseOpenTelemetryPrometheusScrapingEndpoint();

app.Run();




    