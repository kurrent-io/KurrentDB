// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using System.ComponentModel;
using System.Net;
using DotNext.Collections.Generic;
using Kurrent.Surge;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Certificates;
using KurrentDB.Core.Configuration;
using KurrentDB.Core.Messages;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace KurrentDB.Surge.Testing.TUnit;

public class ClusterVNodeApp : IAsyncDisposable {
    static readonly Dictionary<string, string?> DefaultSettings = new() {
        { "KurrentDB:Application:TelemetryOptout", "true" },
        { "KurrentDB:Application:Insecure", "true" },
        { "KurrentDB:Database:MemDb", "true" },
        // super hack to ignore the db's absurd logging config
        { "KurrentDB:Logging:LogLevel", "Default" },
        { "KurrentDB:Logging:DisableLogFile", "true" },
        { "KurrentDB:Interface:DisableAdminUi", "true" },
        { "KurrentDB:DevMode:Dev", "true" }
    };

    WebApplication? App { get; set; }

    public WebApplication? WebApp => App;
    public IEnumerable<string> ServerUrls => App?.Urls ?? Enumerable.Empty<string>();

    public async Task<(ClusterVNodeOptions Options, IServiceProvider Services)> Start(TimeSpan? readinessTimeout = null, Dictionary<string, string?>? overrides = null, Action<IServiceCollection>? configureServices = null) {
        var settings = overrides is not null
            ? DefaultSettings.ToDictionary().With(x => overrides.ForEach((key, value) => x[key] = value))
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

        builder.WebHost.ConfigureKestrel(serverOptions => serverOptions
	        .ListenAnyIP(0, listenOptions => listenOptions.Protocols = HttpProtocols.Http2));

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
        return (configurationRoot.GetRequiredSection("KurrentDB").Get<ClusterVNodeOptions>() ?? new()) with {
            ConfigurationRoot = configurationRoot
            // Unknown           = ClusterVNodeOptions.UnknownOptions.FromConfiguration(configurationRoot.GetRequiredSection("EventStore")),
            // LoadedOptions     = ClusterVNodeOptions.GetLoadedOptions(configurationRoot)
        };
    }

    class NodeReadinessProbe : IHandle<SystemMessage.SystemReady> {
	    static readonly Serilog.ILogger Log = Serilog.Log.Logger.ForContext<NodeReadinessProbe>();

	    TaskCompletionSource Ready { get; } = new();

	    void IHandle<SystemMessage.SystemReady>.Handle(SystemMessage.SystemReady message) {
		    if (!Ready.Task.IsCompleted)
			    Ready.TrySetResult();
	    }

	    async Task WaitUntilReadyAsync(ClusterVNode node, TimeSpan? timeout = null) {
		    node.MainBus.Subscribe(this);

		    using var cancellator = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30)).With(x => {
			    x.Token.Register(() => {
				    Ready.TrySetException(new Exception("Node not ready in time."));
				    node.MainBus.Unsubscribe(this);
			    });
		    });

		    await Ready.Task.ContinueWith(t => {
			    if (t.IsCompletedSuccessfully) {
				    Log.Verbose("Node is ready.");
				    node.MainBus.Unsubscribe(this);
				    Log.Verbose("Unsubscribed from the bus.");
			    }
		    });
	    }

	    public static Task WaitUntilReady(ClusterVNode node, TimeSpan? timeout = null) =>
		    new NodeReadinessProbe().WaitUntilReadyAsync(node, timeout);
    }
}
