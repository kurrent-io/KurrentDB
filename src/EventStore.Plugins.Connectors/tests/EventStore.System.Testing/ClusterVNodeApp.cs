using System.ComponentModel;
using System.Net;
using DotNext.Collections.Generic;
using EventStore.ClusterNode;
using EventStore.Core;
using EventStore.Core.Certificates;
using EventStore.Core.Configuration;
using EventStore.System.Testing.Fixtures;
using EventStore.Toolkit.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace EventStore.System.Testing;

public class ClusterVNodeApp : IAsyncDisposable {
    static readonly Dictionary<string, string?> DefaultSettings = new() {
        { "EventStore:Application:TelemetryOptout", "true" },
        { "EventStore:Application:Insecure", "true" },
        { "EventStore:Database:MemDb", "true" },
        // super hack to ignore esdb's absurd logging config
        { "EventStore:Logging:LogLevel", Common.Options.LogLevel.Default.ToString() },
        { "EventStore:Logging:DisableLogFile", "true" },
        { "EventStore:Interface:DisableAdminUi", "true" },
        { "EventStore:DevMode:Dev", "true" }
    };

    WebApplication? App { get; set; }

    public async Task<(ClusterVNodeOptions Options, IServiceProvider Services)> Start(TimeSpan? readinessTimeout = null, Dictionary<string, string?>? overrides = null, Action<IServiceCollection>? configureServices = null) {
        var settings = overrides is not null
            ? DefaultSettings.With(x => overrides.ForEach((key, value) => x[key] = value))
            : DefaultSettings;

        var options = GetClusterVNodeOptions(settings);

        var esdb = new ClusterVNodeHostedService(options, new OptionsCertificateProvider(), options.ConfigurationRoot);

        var builder = WebApplication.CreateSlimBuilder()
            .With(x => {
                x.Logging.ClearProviders();
                x.Logging.AddSerilog(Log.Logger);
            })
            .With(x => esdb.Node.Startup.ConfigureServices(x.Services))
            .With(x => x.Services.AddSingleton<IHostedService>(esdb))
            .With(x => configureServices?.Invoke(x.Services));

        App = builder.Build().With(x => esdb.Node.Startup.Configure(x));

        await App.StartAsync();

        await NodeReadinessProbe.WaitUntilReady(esdb.Node, readinessTimeout);

        return (options, App.Services);
    }

    public async ValueTask DisposeAsync() {
        if (App is not null)
            await App.DisposeAsync();
    }

    static ClusterVNodeOptions GetClusterVNodeOptions(Dictionary<string, string?> settings) {
        var configurationRoot = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        // required because of a bug in the configuration system that
        // is not reading the attribute from the property itself
        TypeDescriptor.AddAttributes(typeof(EndPoint[]), new TypeConverterAttribute(typeof(GossipSeedConverter)));
        TypeDescriptor.AddAttributes(typeof(IPAddress), new TypeConverterAttribute(typeof(IPAddressConverter)));

        // because we use full keys everything is mapped correctly
        return (configurationRoot.GetRequiredSection("EventStore").Get<ClusterVNodeOptions>() ?? new()) with {
            ConfigurationRoot = configurationRoot
            // Unknown           = ClusterVNodeOptions.UnknownOptions.FromConfiguration(configurationRoot.GetRequiredSection("EventStore")),
            // LoadedOptions     = ClusterVNodeOptions.GetLoadedOptions(configurationRoot)
        };
    }
}