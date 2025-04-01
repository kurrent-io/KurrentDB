// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Runtime;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.DevCertificates;
using EventStore.Common.Exceptions;
using EventStore.Common.Log;
using EventStore.Common.Utils;
using EventStore.Core;
using EventStore.Core.Certificates;
using EventStore.Core.Configuration;
using EventStore.Core.Configuration.Sources;
using EventStore.Core.Services.Transport.Http;
using KurrentDB;
using KurrentDB.Components;
using KurrentDB.Services;
using KurrentDB.Tools;
using KurrentDB.UI.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MudBlazor;
using MudBlazor.Services;
using Serilog;
using Serilog.Events;
using RuntimeInformation = System.Runtime.RuntimeInformation;

var optionsWithLegacyDefaults = LocationOptionWithLegacyDefault.SupportedLegacyLocations;
var configuration = KurrentConfiguration.Build(optionsWithLegacyDefaults, args);

ThreadPool.SetMaxThreads(1000, 1000);
var exitCodeSource = new TaskCompletionSource<int>();

Log.Logger = EventStoreLoggerConfiguration.ConsoleLog;
try {
	var options = ClusterVNodeOptions.FromConfiguration(configuration);

	EventStoreLoggerConfiguration.Initialize(options.Logging.Log, options.GetComponentName(),
		options.Logging.LogConsoleFormat,
		options.Logging.LogFileSize,
		options.Logging.LogFileInterval,
		options.Logging.LogFileRetentionCount,
		options.Logging.DisableLogFile,
		options.Logging.LogConfig);

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
		var result = manager.EnsureDevelopmentCertificate(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1));
		if (result is not (EnsureCertificateResult.Succeeded or EnsureCertificateResult.ValidCertificatePresent)) {
			Log.Fatal("Could not ensure dev certificate is available. Reason: {result}", result);
			return 1;
		}

		var userCerts = manager.ListCertificates(StoreName.My, StoreLocation.CurrentUser, true);
		var machineCerts = manager.ListCertificates(StoreName.My, StoreLocation.LocalMachine, true);
		var certs = userCerts.Concat(machineCerts).ToList();

		if (!certs.Any()) {
			Log.Fatal("Could not create dev certificate.");
			return 1;
		}

		if (!manager.IsTrusted(certs[0]) && RuntimeInformation.IsWindows) {
			Log.Information("Dev certificate {cert} is not trusted. Adding it to the trusted store.", certs[0]);
			manager.TrustCertificate(certs[0]);
		} else {
			Log.Warning("Automatically trusting dev certs is only supported on Windows.\n" +
			            "Please trust certificate {cert} if it's not trusted already.", certs[0]);
		}

		Log.Information("Running in dev mode using certificate '{cert}'", certs[0]);
		certificateProvider = new DevCertificateProvider(certs[0]);
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
						ConfigureHttpOptions(listenOptions, hostedService, useHttps: !hostedService.Node.DisableHttps));

					if (hostedService.Node.EnableUnixSocket)
						TryListenOnUnixSocket(hostedService, server);
				});
			hostedService.Node.Startup.ConfigureServices(builder.Services);
			// Order is important, configure IHostedService after the WebHost to make the sure
			// ClusterVNodeHostedService and the subsystems are started after configuration is finished.
			// Allows the subsystems to resolve dependencies out of the DI in Configure() before being started.
			// Later it may be possible to use constructor injection instead if it fits with the bootstrapping strategy.
			builder.Services.AddSingleton<IHostedService>(hostedService);
			builder.Services.AddSingleton<Preferences>();
			builder.Services
				.AddRazorComponents()
				.AddInteractiveServerComponents()
				.AddInteractiveWebAssemblyComponents();
			builder.Services.AddCascadingAuthenticationState();
			builder.Services.AddMudServices();
			builder.Services.AddMudMarkdownServices();
			builder.Services.AddSingleton(options);
			builder.Services.AddScoped<LogObserver>();
			builder.Services.AddScoped<IdentityRedirectManager>();
			builder.Services.AddSingleton(monitoringService);
			builder.Services.AddSingleton(metricsObserver);

			Log.Information("Environment Name: {0}", builder.Environment.EnvironmentName);
			Log.Information("ContentRoot Path: {0}", builder.Environment.ContentRootPath);

			var app = builder.Build();
			if (app.Environment.IsDevelopment()) {
				app.UseWebAssemblyDebugging();
			}

			hostedService.Node.Startup.Configure(app);
			app.MapRazorComponents<App>()
				.DisableAntiforgery()
				.AddInteractiveServerRenderMode()
				.AddInteractiveWebAssemblyRenderMode()
				.AddAdditionalAssemblies(typeof(KurrentDB.UI._Imports).Assembly);
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

static void ConfigureHttpOptions(ListenOptions listenOptions, ClusterVNodeHostedService hostedService, bool useHttps) {
	if (useHttps)
		listenOptions.UseHttps(CreateServerOptionsSelectionCallback(hostedService), null);
	else
		listenOptions.Use(next => new ClearTextHttpMultiplexingMiddleware(next).OnConnectAsync);
}

static void TryListenOnUnixSocket(ClusterVNodeHostedService hostedService, KestrelServerOptions server) {
	if (hostedService.Node.Db.Config.InMemDb) {
		Log.Information("Not listening on a UNIX domain socket since the database is running in memory.");
		return;
	}

	if (!RuntimeInformation.IsLinux && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17063)) {
		Log.Error("Not listening on a UNIX domain socket since it is not supported by the operating system.");
		return;
	}

	try {
		var legacyUnixSocket = Path.GetFullPath(Path.Combine(hostedService.Node.Db.Config.Path, "eventstore.sock"));
		var unixSocket = Path.GetFullPath(Path.Combine(hostedService.Node.Db.Config.Path, "kurrent.sock"));

		CleanupStaleSocket(legacyUnixSocket);
		CleanupStaleSocket(unixSocket);

		server.ListenUnixSocket(unixSocket, listenOptions => {
			listenOptions.Use(next => new UnixSocketConnectionMiddleware(next).OnConnectAsync);
			ConfigureHttpOptions(listenOptions, hostedService, useHttps: false);
		});
		Log.Information("Listening on UNIX domain socket: {unixSocket}", unixSocket);
	} catch (Exception ex) {
		Log.Error(ex, "Failed to listen on UNIX domain socket.");
		throw;
	}

	return;

	void CleanupStaleSocket(string socketPath) {
		if (File.Exists(socketPath)) {
			try {
				File.Delete(socketPath);
				Log.Information("Cleaned up stale UNIX domain socket: {unixSocket}", socketPath);
			} catch (Exception ex) {
				Log.Error(ex, "Failed to clean up stale UNIX domain socket: {unixSocket}. Please delete the file manually.", socketPath);
				throw;
			}
		}
	}
}

static ServerOptionsSelectionCallback CreateServerOptionsSelectionCallback(ClusterVNodeHostedService hostedService) {
	return (_, _, _, _) => {
		var serverOptions = new SslServerAuthenticationOptions {
			ServerCertificateContext = SslStreamCertificateContext.Create(
				hostedService.Node.CertificateSelector(),
				hostedService.Node.IntermediateCertificatesSelector(),
				offline: true),
			ClientCertificateRequired = true, // request a client certificate but it's not necessary for the client to supply one
			RemoteCertificateValidationCallback = (_, certificate, chain, sslPolicyErrors) => {
				if (certificate == null) // not necessary to have a client certificate
					return true;

				var (isValid, error) =
					hostedService.Node.InternalClientCertificateValidator(
						certificate,
						chain,
						sslPolicyErrors);
				if (!isValid && error != null) {
					Log.Error("Client certificate validation error: {e}", error);
				}

				return isValid;
			},
			CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
			EnabledSslProtocols = SslProtocols.None, // let the OS choose a secure TLS protocol
			ApplicationProtocols = [SslApplicationProtocol.Http2, SslApplicationProtocol.Http11],
			AllowRenegotiation = false
		};

		return ValueTask.FromResult(serverOptions);
	};
}
