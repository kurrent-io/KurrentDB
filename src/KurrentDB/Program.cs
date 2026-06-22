// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB;
using KurrentDB.Common.DevCertificates;
using KurrentDB.Common.Exceptions;
using KurrentDB.Common.Log;
using KurrentDB.Common.Utils;
using KurrentDB.Components;
using KurrentDB.Components.Cluster;
using KurrentDB.Components.Dashboard;
using KurrentDB.Components.PersistentSubscriptions;
using KurrentDB.Components.Plugins;
using KurrentDB.Components.Projections;
using KurrentDB.Components.Scavenges;
using KurrentDB.Components.ServerInfo;
using KurrentDB.Components.Streams;
using KurrentDB.Components.Users;
using KurrentDB.Core;
using KurrentDB.Core.Certificates;
using KurrentDB.Core.Configuration;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Logging;
using KurrentDB.Services;
using KurrentDB.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;
using MudBlazor;
using MudBlazor.Services;
using Serilog;
using Serilog.Events;
using RuntimeInformation = System.Runtime.RuntimeInformation;

var optionsWithLegacyDefaults = LocationOptionWithLegacyDefault.SupportedLegacyLocations;
var configuration = KurrentConfiguration.Build(optionsWithLegacyDefaults, args);

var exitCodeSource = new TaskCompletionSource<int>();

Log.Logger = KurrentLoggerConfiguration.ConsoleLog;
try {
	var options = ClusterVNodeOptions.FromConfiguration(configuration);

	Log.Logger = KurrentLoggerConfiguration
		.CreateLoggerConfiguration(options.Logging, options.GetComponentName())
		.AddOpenTelemetryLogger(configuration, options.GetComponentName())
		.CreateLogger();

	ConfigureThreadPool();

	if (options.Application.Help) {
		await Console.Out.WriteLineAsync(ClusterVNodeOptions.HelpText);
		return 0;
	}

	if (options.Application.Version) {
		await Console.Out.WriteLineAsync(VersionInfo.Text);
		return 0;
	}

	if (options.DevMode.RemoveDevCerts) {
		Log.Information("Removing KurrentDB dev certs.");
		CertificateManager.Instance.CleanupHttpsCertificates();
		Log.Information("Dev certs removed. Exiting.");
		return 0;
	}

	Log.Information(
		"{description,-25} {version} {edition} ({buildId}/{commitSha}, {timestamp})", "DB VERSION:",
		VersionInfo.Version, VersionInfo.Edition, VersionInfo.BuildId, VersionInfo.CommitSha, VersionInfo.Timestamp
	);

	Log.Information("{description,-25} {osArchitecture} ", "OS ARCHITECTURE:", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture);
	Log.Information("{description,-25} {osFlavor} ({osVersion})", "OS:", RuntimeInformation.OsPlatform, Environment.OSVersion);
	Log.Information("{description,-25} {osRuntimeVersion} ({architecture}-bit)", "RUNTIME:", RuntimeInformation.RuntimeVersion, RuntimeInformation.RuntimeMode);
	Log.Information("{description,-25} {maxGeneration} IsServerGC: {isServerGC} Latency Mode: {latencyMode}", "GC:",
		GC.MaxGeneration == 0 ? "NON-GENERATION (PROBABLY BOEHM)" : $"{GC.MaxGeneration + 1} GENERATIONS",
		GCSettings.IsServerGC,
		GCSettings.LatencyMode);
	Log.Information("{description,-25} {logsDirectory}", "LOGS:", options.Logging.Log);
	Log.Information("{description,-25} {isWindowsService}", "IsWindowsService:", WindowsServiceHelpers.IsWindowsService());

	var gcSettings = string.Join($"{Environment.NewLine}    ", GC.GetConfigurationVariables().Select(kvp => $"{kvp.Key}: {kvp.Value}"));
	Log.Information($"GC Configuration settings:{Environment.NewLine}    {{settings}}", gcSettings);

	Log.Information(options.DumpOptions()!);

	var level = options.Application.AllowUnknownOptions
		? LogEventLevel.Warning
		: LogEventLevel.Fatal;

	foreach (var (option, suggestion) in options.Unknown.Options) {
		if (string.IsNullOrEmpty(suggestion)) {
			Log.Write(level, "The option {option} is not a known option.", option);
		} else {
			Log.Write(level, "The option {option} is not a known option. Did you mean {suggestion}?", option, suggestion);
		}
	}

	if (options.UnknownOptionsDetected && !options.Application.AllowUnknownOptions) {
		Log.Fatal(
			$"Found unknown options. To continue anyway, set {nameof(ClusterVNodeOptions.ApplicationOptions.AllowUnknownOptions)} to true.");
		Log.Information("Use the --help option in the command line to see the full list of KurrentDB configuration options.");
		return 1;
	}

	CertificateProvider certificateProvider;
	if (options.DevMode.Dev) {
		Log.Information("Dev mode is enabled.");
		Log.Warning(
			"\n==============================================================================================================\n" +
			"DEV MODE IS ON. THIS MODE IS *NOT* RECOMMENDED FOR PRODUCTION USE.\n" +
			"DEV MODE WILL GENERATE AND TRUST DEV CERTIFICATES FOR RUNNING A SINGLE SECURE NODE ON LOCALHOST.\n" +
			"==============================================================================================================\n");
		var manager = CertificateManager.Instance;
		var devCertPath = options.DevMode.DevCertPath;
		X509Certificate2 devCert = null;

		// If a cert path is specified, try to load an existing cert from it
		if (!string.IsNullOrEmpty(devCertPath)) {
			devCert = DevCertificateFile.TryLoad(devCertPath);
			if (devCert is not null) {
				Log.Information("Dev certificate loaded from {path}", devCertPath);
			} else if (File.Exists(devCertPath)) {
				Log.Warning("Dev certificate at {path} is invalid or expired, generating a new one.", devCertPath);
			}
		}

		if (devCert is null) {
			// Generate a new certificate and optionally export to file
			var result = manager.EnsureDevelopmentCertificate(
				DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1),
				out devCert,
				path: devCertPath,
				includePrivateKey: !string.IsNullOrEmpty(devCertPath));
			if (result is not (EnsureCertificateResult.Succeeded or EnsureCertificateResult.ValidCertificatePresent)) {
				Log.Fatal("Could not ensure dev certificate is available. Reason: {result}", result);
				return 1;
			}

			if (devCert is null) {
				Log.Fatal("Could not create dev certificate. " +
						  "If the home directory is not writable (e.g., in a container), " +
						  "use --dev-cert-path to specify an alternative file location.");
				return 1;
			}

			if (!string.IsNullOrEmpty(devCertPath)) {
				Log.Information("Dev certificate saved to {path}", devCertPath);
			}
		}

		// Write public cert as .crt for clients to trust
		if (!string.IsNullOrEmpty(devCertPath)) {
			try {
				var crtPath = DevCertificateFile.WritePublicCertificate(devCert, devCertPath);
				Log.Information("Dev certificate public key saved to {path} (use this to configure client trust)",
					crtPath);
			} catch (Exception ex) {
				Log.Warning("Could not write public certificate: {error}", ex.Message);
			}
		}

		if (!manager.IsTrusted(devCert) && RuntimeInformation.IsWindows) {
			Log.Information("Dev certificate {cert} is not trusted. Adding it to the trusted store.", devCert);
			manager.TrustCertificate(devCert);
		} else if (!RuntimeInformation.IsWindows) {
			Log.Warning("Automatically trusting dev certs is only supported on Windows.\n" +
						"Please trust certificate {cert} if it's not trusted already.", devCert);
		}

		Log.Information("Running in dev mode using certificate '{cert}'", devCert);
		certificateProvider = new DevCertificateProvider(devCert);
	} else {
		certificateProvider = new OptionsCertificateProvider();
	}

	var defaultLocationWarnings = options.CheckForLegacyDefaultLocations(optionsWithLegacyDefaults);
	foreach (var locationWarning in defaultLocationWarnings) {
		Log.Warning(locationWarning);
	}

	var eventStoreOptionWarnings = options.CheckForLegacyEventStoreConfiguration();
	if (eventStoreOptionWarnings.Any()) {
		Log.Warning(
			$"The \"{KurrentConfigurationKeys.LegacyEventStorePrefix}\" configuration root " +
			$"has been deprecated and renamed to \"{KurrentConfigurationKeys.Prefix}\". " +
			"The following settings will still be used, but will stop working in a future release:");
		foreach (var warning in eventStoreOptionWarnings) {
			Log.Warning(warning);
		}
	}

	var deprecationWarnings = options.GetDeprecationWarnings();
	if (deprecationWarnings != null) {
		Log.Warning($"DEPRECATED{Environment.NewLine}{deprecationWarnings}");
	}

	if (!ClusterVNodeOptionsValidator.ValidateForStartup(options)) {
		return 1;
	}

	if (options.Application.Insecure) {
		Log.Warning(
			"\n==============================================================================================================\n" +
			"INSECURE MODE IS ON. THIS MODE IS *NOT* RECOMMENDED FOR PRODUCTION USE.\n" +
			"INSECURE MODE WILL DISABLE ALL AUTHENTICATION, AUTHORIZATION AND TRANSPORT SECURITY FOR ALL CLIENTS AND NODES.\n" +
			"==============================================================================================================\n");
	} else if (options.Application.DisableTls) {
		Log.Warning(
			"\n==============================================================================================================\n" +
			"TLS IS DISABLED. AUTHENTICATION CREDENTIALS WILL BE TRANSMITTED IN CLEARTEXT.\n" +
			"THIS MODE IS *NOT* RECOMMENDED FOR PRODUCTION USE.\n" +
			"==============================================================================================================\n");
	}

	if (options.Application.WhatIf) {
		return 0;
	}

	using var cts = new CancellationTokenSource();
	var token = cts.Token;
	Application.RegisterExitAction(code => {
		// add a small delay to allow the host to start up in case there's a premature shutdown
		cts.CancelAfter(TimeSpan.FromSeconds(1));
		exitCodeSource.SetResult(code);
	});

	using (var hostedService = new ClusterVNodeHostedService(options, certificateProvider, configuration)) {
		// Synchronous Wait() because ClusterVNodeHostedService must be disposed on the same thread
		// that it was constructed on, because it makes use of ExclusiveDbLock which uses a Mutex.
		// ReSharper disable once MethodHasAsyncOverloadWithCancellation
		// ReSharper disable once MethodSupportsCancellation
		Run(hostedService).Wait();
	}

	return await exitCodeSource.Task;

	async Task Run(ClusterVNodeHostedService hostedService) {
		var monitoringService = new MonitoringService();
		var metricsObserver = new MetricsObserver();
		try {
			var applicationOptions = new WebApplicationOptions {
				Args = args,
				ContentRootPath = AppDomain.CurrentDomain.BaseDirectory
			};

			var builder = WebApplication.CreateBuilder(applicationOptions);
			builder.Configuration.AddConfiguration(configuration);
			// AddWindowsService adds EventLog logging, which we remove afterwards.
			builder.Services.AddWindowsService();
			builder.Logging.ClearProviders().AddSerilog();
			builder.Services.Configure<KestrelServerOptions>(configuration.GetSection("Kestrel"));
			builder.Services.Configure<HostOptions>(x => {
				x.ShutdownTimeout = ClusterVNode.ShutdownTimeout + TimeSpan.FromSeconds(1);
#if DEBUG
				x.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
#else
				x.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
#endif
			});
			builder.WebHost.ConfigureKestrel(
				server => {
					server.Limits.Http2.KeepAlivePingDelay = TimeSpan.FromMilliseconds(options.Grpc.KeepAliveInterval);
					server.Limits.Http2.KeepAlivePingTimeout = TimeSpan.FromMilliseconds(options.Grpc.KeepAliveTimeout);

					server.Listen(options.Interface.NodeIp, options.Interface.NodePort, listenOptions =>
						KestrelHelpers.ConfigureHttpOptions(listenOptions, hostedService, useHttps: !hostedService.Node.DisableHttps));

					if (hostedService.Node.EnableUnixSocket)
						KestrelHelpers.TryListenOnUnixSocket(hostedService, server);
				});
			hostedService.Node.Startup.ConfigureServices(builder.Services);
			// Order is important, configure IHostedService after the WebHost to make the sure
			// ClusterVNodeHostedService and the subsystems are started after configuration is finished.
			// Allows the subsystems to resolve dependencies out of the DI in Configure() before being started.
			// Later it may be possible to use constructor injection instead if it fits with the bootstrapping strategy.
			builder.Services.AddSingleton<IHostedService>(hostedService);
			// Scoped, not singleton: theme is a per-user (per-circuit) preference, not server-global.
			// HttpContextAccessor lets Preferences read the theme cookie server-side to seed flicker-free.
			builder.Services.AddHttpContextAccessor();
			// Page authorization: map [Authorize(Policy = UiPolicies.X)] to a KurrentDB Operation check.
			builder.Services.AddAuthorization(KurrentDB.Components.Shared.UiPolicies.Configure);
			builder.Services.AddSingleton<
				Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
				KurrentDB.Components.Shared.OperationAuthorizationHandler>();
			builder.Services.AddScoped<KurrentDB.UI.Services.Preferences>();
			builder.Services
				.AddRazorComponents()
				.AddInteractiveServerComponents();
			builder.Services.AddCascadingAuthenticationState();
			builder.Services.AddMudServices(config => {
				config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomRight;
			});
			builder.Services.AddMudMarkdownServices();
			builder.Services.AddScoped<LogObserver>();
			builder.Services.AddScoped<ClipboardService>();
			builder.Services.AddSingleton(monitoringService);
			builder.Services.AddSingleton(metricsObserver);
			builder.Services.AddSingleton<PluginsService>();
			builder.Services.AddScoped<UserManagementService>();
			builder.Services.AddScoped<ClusterOperationsService>();
			// Process-wide node-role tracker (subscribes to $mem-node-state); shared by all UI circuits.
			builder.Services.AddSingleton<KurrentDB.Components.Cluster.GossipMonitor>();
			builder.Services.AddSingleton<IHostedService>(sp =>
				sp.GetRequiredService<KurrentDB.Components.Cluster.GossipMonitor>());
			builder.Services.AddScoped<ScavengeService>();
			builder.Services.AddScoped<DashboardService>();
			builder.Services.AddScoped<StreamsService>();
			builder.Services.AddScoped(sp => {
				// ProjectionsService publishes to the projections subsystem's leader input queue. When projections
				// are disabled on this node (e.g. --run-projections=None) the subsystem is absent; resolve a service
				// in the "unavailable" state (null queue) rather than throwing, so injecting it into the Projections
				// page doesn't crash the circuit — the page shows a calm "not enabled" message instead.
				var opts = sp.GetRequiredService<ClusterVNodeOptions>();
				var projectionsPublisher = opts.Subsystems.OfType<KurrentDB.Projections.Core.ProjectionsSubsystem>().FirstOrDefault()?.LeaderInputQueue;
				return new ProjectionsService(projectionsPublisher, sp.GetRequiredService<EventStore.Plugins.Authorization.IAuthorizationProvider>());
			});
			builder.Services.AddScoped<PersistentSubscriptionsService>();
			builder.Services.AddScoped<ServerInfoService>();
			// UI-layer authorizing wrapper over the SecondaryIndexing StatsService (which does no auth of its
			// own). Resolved lazily by StatsPage and only when the plugin is enabled, so registering it here
			// unconditionally is safe even though the inner StatsService is only registered by the plugin.
			builder.Services.AddScoped<KurrentDB.Components.Stats.UiStatsService>();
			builder.Services.AddSingleton(TimeProvider.System);
			Log.Information("Environment Name: {0}", builder.Environment.EnvironmentName);
			Log.Information("ContentRoot Path: {0}", builder.Environment.ContentRootPath);

			var app = builder.Build();

			hostedService.Node.Startup.Configure(app);
			if (!options.Interface.DisableAdminUi) {
				app.MapStaticAssets();
				app.MapRazorComponents<App>()
					.DisableAntiforgery()
					.AddInteractiveServerRenderMode();
			}
			await app.RunAsync(token);

			exitCodeSource.TrySetResult(0);
		} catch (OperationCanceledException) {
			exitCodeSource.TrySetResult(0);
		} catch (Exception ex) {
			Log.Fatal(ex, "Exiting");
			exitCodeSource.TrySetResult(1);
		}
	}
} catch (InvalidConfigurationException ex) {
	Log.Fatal("Invalid Configuration: " + ex.Message);
	return 1;
} catch (Exception ex) {
	Log.Fatal(ex, "Host terminated unexpectedly.");
	return 1;
} finally {
	await Log.CloseAndFlushAsync();
}

void ConfigureThreadPool() {
	ThreadPool.GetMinThreads(out var minWorkerThreads1, out var minCompletionPortThreads1);
	ThreadPool.GetMaxThreads(out var maxWorkerThreads1, out var maxCompletionPortThreads1);

	// todo: consider setting min threads. this would not create them up front, but allows them to be created quickly on demand without hill climbing.
	ThreadPool.SetMaxThreads(1000, 1000);

	ThreadPool.GetMinThreads(out var minWorkerThreads2, out var minCompletionPortThreads2);
	ThreadPool.GetMaxThreads(out var maxWorkerThreads2, out var maxCompletionPortThreads2);

	Log.Information("Changed MinWorkerThreads from {Before} to {After}", minWorkerThreads1, minWorkerThreads2);
	Log.Information("Changed MaxWorkerThreads from {Before} to {After}", maxWorkerThreads1, maxWorkerThreads2);
	Log.Information("Changed MinCompletionPortThreads from {Before} to {After}", minCompletionPortThreads1, minCompletionPortThreads2);
	Log.Information("Changed MaxCompletionPortThreads from {Before} to {After}", maxCompletionPortThreads1, maxCompletionPortThreads2);
}
