// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.ComponentModel;
using System.Net;
using DotNext.Collections.Generic;
using KurrentDB.Testing;
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

namespace KurrentDB.Api.Tests;

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

    public WebApplication? Web { get; private set; }


    public async Task<(ClusterVNodeOptions Options, IServiceProvider Services)> Start(
	    TimeSpan? readinessTimeout = null,
	    Dictionary<string, string?>? overrides = null,
	    Action<IServiceCollection>? configureServices = null
	) {
        var settings = overrides is not null
            ? DefaultSettings.ToDictionary().With(x => overrides.ForEach((key, value) => x[key] = value))
            : DefaultSettings;

        var options = GetClusterVNodeOptions(settings);

        var kdb = new ClusterVNodeHostedService(options, new OptionsCertificateProvider(), options.ConfigurationRoot);

        var builder = WebApplication.CreateSlimBuilder()
            .With(x => x.Logging.ClearProviders().AddSerilog(Log.Logger))
            .With(x => kdb.Node.Startup.ConfigureServices(x.Services))
            .With(x => x.Services.AddSingleton<IHostedService>(kdb))
            .With(x => configureServices?.Invoke(x.Services));

        builder.WebHost.ConfigureKestrel(serverOptions => serverOptions
	        .ListenAnyIP(0, listenOptions => listenOptions.Protocols = HttpProtocols.Http2));

        Web = builder.Build().With(x => kdb.Node.Startup.Configure(x));

        await Web.StartAsync();

        await NodeReadinessProbe.WaitUntilReady(kdb.Node, readinessTimeout);

        return (options, Web.Services);
    }

    public async ValueTask DisposeAsync() {
        if (Web is not null)
            await Web.DisposeAsync();
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
			    if (!t.IsCompletedSuccessfully) return;
			    Log.Verbose("Node is ready.");
			    node.MainBus.Unsubscribe(this);
			    Log.Verbose("Unsubscribed from the bus.");
		    }, cancellator.Token);
	    }

	    public static Task WaitUntilReady(ClusterVNode node, TimeSpan? timeout = null) =>
		    new NodeReadinessProbe().WaitUntilReadyAsync(node, timeout);
    }
}
