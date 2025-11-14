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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using Serilog;

using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace KurrentDB.Testing;

/// <summary>
/// Represents a single node in a KurrentDB cluster for testing purposes.
/// This class sets up an in-memory KurrentDB instance with configurable options,
/// allowing for isolated testing of cluster behaviors and interactions.
/// <remarks>
/// Important: AuthZ and AuthN are not enforced, because of lack of macOS support for self-signed certificate.
/// </remarks>
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

    static readonly Serilog.ILogger Log = Serilog.Log.ForContext<ClusterVNodeApp>();

	static ClusterVNodeApp() {
		// required because of a bug in the configuration system that
		// is not reading the attributes from the respective properties
		TypeDescriptor.AddAttributes(typeof(EndPoint[]), new TypeConverterAttribute(typeof(GossipSeedConverter)));
		TypeDescriptor.AddAttributes(typeof(EndPoint),   new TypeConverterAttribute(typeof(GossipEndPointConverter)));
		TypeDescriptor.AddAttributes(typeof(IPAddress),  new TypeConverterAttribute(typeof(IPAddressConverter)));
	}

	long _started;

    CertificateProvider ConfigureCertificateProvider(ClusterVNodeOptions options) {
        return new OptionsCertificateProvider();

        //TODO SS: enable dev certs to allow proper auth testing once we are able to support macOS where it currently fails

        // if (!options.DevMode.Dev)
        //     return new OptionsCertificateProvider();
        //
        // var result = CertificateManager.Instance
        //     .EnsureDevelopmentCertificate(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1), trust: true);
        //
        // if (result is not (EnsureCertificateResult.Succeeded or EnsureCertificateResult.ValidCertificatePresent))
        //     throw new Exception($"Failed to create or validate the development certificate. Result: {result}");
        //
        // var certs = CertificateManager.Instance.ListCertificates(StoreName.My, StoreLocation.CurrentUser, true)
        //     .Concat(CertificateManager.Instance.ListCertificates(StoreName.My, StoreLocation.LocalMachine, true))
        //     .ToList();
        //
        // if (certs.Count == 0)
        //     throw new Exception("Failed to find the development certificate after it was just created.");
        //
        // Log.Information(
        //     "Found {Count} development certificates in the system: {Certificates}",
        //     certs.Count, certs.Select(c => $"{c.Thumbprint}: {c.FriendlyName}"));
        //
        // Log.Information(
        //     "Running in dev mode using {Trusted} certificate: {Certificate}",
        //     CertificateManager.Instance.IsTrusted(certs[0]) ? "trusted" : "untrusted", certs[0]);
        //
        // if (!CertificateManager.Instance.IsTrusted(certs[0]) && RuntimeInformation.IsWindows) {
        //     Log.Information("Dev certificate {cert} is not trusted. Adding it to the trusted store.", certs[0]);
        //     CertificateManager.Instance.TrustCertificate(certs[0]);
        // }
        // else {
        //     Log.Warning(
        //         "Automatically trusting dev certs is only supported on Windows.\n" +
        //         "Please trust certificate {cert} if it's not trusted already.", certs[0]
        //     );
        // }
        //
        // return new DevCertificateProvider(certs[0]);
    }

	/// <summary>
	/// Initializes a new instance of the <see cref="ClusterVNodeApp"/> class.
	/// </summary>
	public ClusterVNodeApp(Action<ClusterVNodeOptions, IServiceCollection>? configureServices = null, Dictionary<string, object?>? overrides = null) {
		ServerOptions = GetOptions(DefaultSettings, overrides);

        if (ServerOptions.DevMode.Dev) {
            const string logMessage = """
                ==============================================================================================================
                DEV MODE ENABLED 
                ==============================================================================================================
                """;

            Log.Warning(logMessage);
        }

		var svc = new ClusterVNodeHostedService(
            ServerOptions,
            ConfigureCertificateProvider(ServerOptions),
            ServerOptions.ConfigurationRoot);

		var builder = WebApplication.CreateSlimBuilder();

		// configure logging to use Serilog
		builder.Logging
			.ClearProviders()
			.AddSerilog();

		// configure services first so we can override things
		svc.Node.Startup.ConfigureServices(builder.Services);

        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddOtlpExporter());

        // then add the hosted service
        builder.Services.AddSingleton<IHostedService>(svc);

        // Configures gRPC client address discovery to use a static resolver
        // that points to the test server's address.
        builder.Services.EnableGrpcClientsAddressDiscovery();

        // Configures gRPC clients to use gzip compression for requests
        builder.Services.EnableGrpcClientsCompression();

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
			kestrel.ListenAnyIP(0, listen => {
				KestrelHelpers.ConfigureHttpOptions(
					listenOptions: listen,
					hostedService: svc,
					useHttps: !ServerOptions.Application.Insecure);
			});
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

		static ClusterVNodeOptions GetOptions(Dictionary<string, string?> settings, Dictionary<string, object?>? overrides = null) {
            if (overrides is not null) {
                foreach (var entry in overrides)
                    settings[entry.Key] = entry.Value?.ToString();
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
