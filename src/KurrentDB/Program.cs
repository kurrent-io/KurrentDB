// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB;
using KurrentDB.Common.DevCertificates;
using KurrentDB.Common.Exceptions;
using KurrentDB.Common.Log;
using KurrentDB.Common.Utils;
using KurrentDB.Core;
using KurrentDB.Core.Configuration;
using KurrentDB.Core.Configuration.Sources;
using KurrentDB.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

var optionsWithLegacyDefaults = LocationOptionWithLegacyDefault.SupportedLegacyLocations;
var configuration = KurrentConfiguration.Build(optionsWithLegacyDefaults, args);

var exitCodeSource = new TaskCompletionSource<int>();

KurrentLoggerConfiguration.InstallProcessDefaults();
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

	if (!NodePreflight.TryPrepare(options, out var cannotStart)) {
		Log.Fatal(cannotStart);
		Log.Information("Use the --help option in the command line to see the full list of KurrentDB configuration options.");
		return 1;
	}

	if (!CertificateProviders.TryCreate(options, out var certificateProvider, out var noCertificate)) {
		Log.Fatal(noCertificate);
		return 1;
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

	var hostedService = new ClusterVNodeHostedService(options, certificateProvider, configuration);
	await Run(hostedService);

	return await exitCodeSource.Task;

	async Task Run(ClusterVNodeHostedService hostedService) {
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
			// a node that cannot open a socket still has its TCP endpoint, so the server carries on
			builder.WebHost.ConfigureKestrel(server =>
				KestrelHelpers.TryConfigureListeners(server, options, hostedService, listenOnTcp: true, out _));
			NodeWebApplication.ConfigureServices(builder.Services, hostedService);
			if (!options.Interface.DisableAdminUi)
				EmbeddedUI.ConfigureServices(builder.Services);
			NodePreflight.WriteHostEnvironment(builder.Environment);

			NodeWebApplication.LogHostedServiceLifecycle(builder.Services);

			var app = builder.Build();

			// the shutdown guard has to see a circuit request before the node's endpoints claim it
			if (!options.Interface.DisableAdminUi)
				EmbeddedUI.UseShutdownGuard(app);

			NodeWebApplication.Configure(app, hostedService);

			if (!options.Interface.DisableAdminUi)
				EmbeddedUI.Configure(app);

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
