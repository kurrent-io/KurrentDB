// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

// ReSharper disable CheckNamespace

using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using Humanizer;
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

using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace KurrentDB.Testing;

/// <summary>
/// Represents a single node in a KurrentDB cluster for testing purposes.
/// This class sets up an in-memory KurrentDB instance with configurable options,
/// allowing for isolated testing of cluster behaviors and interactions.
/// </summary>
[PublicAPI]
public class ClusterVNodeApp : IAsyncDisposable {
	static readonly Dictionary<string, string?> DefaultSettings = new() {
        { "KurrentDB:Application:TelemetryOptout", "true" },
        { "KurrentDB:Application:Insecure", "true" },
        { "KurrentDB:Database:MemDb", "true" },
        { "KurrentDB:Interface:DisableAdminUi", "true" },
        { "KurrentDB:DevMode:Dev", "true" },
        // super hack to ignore the db's absurd logging config
        { "KurrentDB:Logging:LogLevel", "Default" },
        { "KurrentDB:Logging:DisableLogFile", "true" }
    };

	static ClusterVNodeApp() {
		// required because of a bug in the configuration system that
		// is not reading the attributes from the respective properties
		TypeDescriptor.AddAttributes(typeof(EndPoint[]), new TypeConverterAttribute(typeof(GossipSeedConverter)));
		TypeDescriptor.AddAttributes(typeof(EndPoint),   new TypeConverterAttribute(typeof(GossipEndPointConverter)));
		TypeDescriptor.AddAttributes(typeof(IPAddress),  new TypeConverterAttribute(typeof(IPAddressConverter)));
	}

	long _started;

	/// <summary>
	/// Initializes a new instance of the <see cref="ClusterVNodeApp"/> class.
	/// </summary>
	public ClusterVNodeApp(Action<ClusterVNodeOptions, IServiceCollection>? configureServices = null, Dictionary<string, string?>? overrides = null) {
		ServerOptions = GetOptions(DefaultSettings, overrides);

		var svc = new ClusterVNodeHostedService(ServerOptions, new OptionsCertificateProvider(), ServerOptions.ConfigurationRoot);

		var builder = WebApplication.CreateSlimBuilder();

		// configure logging to use Serilog
		builder.Logging
			.ClearProviders()
			.AddSerilog();

		// configure services first so we can override things
		svc.Node.Startup.ConfigureServices(builder.Services);

		// then add the hosted service
		builder.Services.AddSingleton<IHostedService>(svc);

		// allow the caller to override anything they want
		// before we do some final configuration of our own
		configureServices?.Invoke(ServerOptions, builder.Services);

		// add readiness probe and host options
		builder.Services
			.AddActivatedSingleton<NodeReadinessProbe>()
			.Configure<HostOptions>(host => {
				host.ShutdownTimeout                    = ClusterVNode.ShutdownTimeout; // this should be configurable in the real world
				host.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
			});

		// configure kestrel to use http2 and the grpc ports
		builder.WebHost.ConfigureKestrel(kestrel => {
			kestrel.ListenAnyIP(0, listen => listen.Protocols = HttpProtocols.Http2);
			kestrel.Limits.Http2.KeepAlivePingDelay   = TimeSpan.FromMilliseconds(ServerOptions.Grpc.KeepAliveInterval);
			kestrel.Limits.Http2.KeepAlivePingTimeout = TimeSpan.FromMilliseconds(ServerOptions.Grpc.KeepAliveTimeout);
		});

		// finally, build the app and configure all the routes and grpc services
		Web = builder.Build();

        svc.Node.Startup.Configure(Web);

		// grab the logger for later use
		Logger = Web.Services.GetRequiredService<ILogger<ClusterVNodeApp>>();

		// fin
		Logger.LogDebug("Server configured");

		return;

		static ClusterVNodeOptions GetOptions(Dictionary<string, string?> settings, Dictionary<string, string?>? overrides = null) {
            if (overrides is not null) {
                foreach (var entry in overrides)
                    settings[entry.Key] = entry.Value;
            }

			var configurationRoot = new ConfigurationBuilder()
				.AddInMemoryCollection(settings)
				.Build();

			// we use full keys so everything is correctly mapped
			return (configurationRoot.GetRequiredSection("KurrentDB").Get<ClusterVNodeOptions>() ?? new()) with {
				ConfigurationRoot = configurationRoot
                // Note: these are all available if needed
				// Unknown           = ClusterVNodeOptions.UnknownOptions.FromConfiguration(configurationRoot.GetRequiredSection("EventStore")),
				// LoadedOptions     = ClusterVNodeOptions.GetLoadedOptions(configurationRoot)
			};
		}
	}

    WebApplication Web    { get; }
    ILogger        Logger { get; }

	/// <summary>
    /// The options used to configure the Server.
    /// </summary>
    public ClusterVNodeOptions ServerOptions { get; }

    /// <summary>
    /// The service provider instance configured for the Server.
    /// </summary>
    public IServiceProvider Services => Web.Services;

    /// <summary>
    /// Starts the server and waits until it is ready to accept requests.
    /// </summary>
    public async Task Start(TimeSpan? readinessTimeout = null) {
	    if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
		    throw new InvalidOperationException("Server already running!");

	    Logger.LogDebug("Server starting...");

        _ = Web.StartAsync();

	    await Web.Services
	        .GetRequiredService<NodeReadinessProbe>()
	        .WaitUntilReadyAsync(readinessTimeout ?? TimeSpan.FromSeconds(10));

        Logger.LogDebug("Server ready");
    }

    protected virtual ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() {
        Logger.LogDebug("Server stopping...");
        await DisposeAsyncCore();
        await Web.StopAsync();
        await Web.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    sealed class NodeReadinessProbe : IHandle<SystemMessage.SystemReady> {
        public NodeReadinessProbe(ISubscriber mainBus) {
            Ready   = new();
            MainBus = mainBus;
            // subscribe immediately so we don't miss anything
            MainBus.Subscribe(this);
        }

        TaskCompletionSource Ready   { get; }
        ISubscriber          MainBus { get; }

	    void IHandle<SystemMessage.SystemReady>.Handle(SystemMessage.SystemReady message) {
            if (!Ready.Task.IsCompleted && Ready.TrySetResult())
                MainBus.Unsubscribe(this);
        }

	    public async ValueTask WaitUntilReadyAsync(TimeSpan timeout) {
            Debug.Assert(timeout > TimeSpan.Zero, "Timeout must be greater than zero.");
		    try {
			    await Ready.Task.WaitAsync(timeout).ConfigureAwait(false);
		    }
		    catch (TimeoutException) {
                throw new TimeoutException(
                    $"Server readiness timed out after {timeout.Humanize()}. " +
                    $"Please check the logs for more details.");
		    }
		    finally {
                if (!Ready.Task.IsCompletedSuccessfully) MainBus.Unsubscribe(this);
		    }
	    }
    }
}
