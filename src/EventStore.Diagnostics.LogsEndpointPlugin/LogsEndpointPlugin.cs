// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using EventStore.Plugins.Diagnostics;
using EventStore.Plugins.Licensing;
using EventStore.Plugins.Subsystems;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Serilog;
using ILogger = Serilog.ILogger;

namespace EventStore.Diagnostics.LogsEndpointPlugin;

// TODO: use SubsystemPlugin now that it is more flexible about licence requirements and what to do when they fail
public class LogsEndpointPlugin : ISubsystemsPlugin, ISubsystem {
	public string Name => "LogsEndpoint";
	public string Version => typeof(LogsEndpointPlugin).Assembly.GetName().Version!.ToString();
	public string CommandLineName => "logs-endpoint";
	public string DiagnosticsName => Name;
	public KeyValuePair<string, object>[] DiagnosticsTags { get; } = [];
	public bool Enabled { get; private set; } = true;
	public string LicensePublicKey => LicenseConstants.LicensePublicKey;

	private static readonly ILogger _logger = Log.ForContext<LogsEndpointPlugin>();
	private const string EndpointPath = "/admin/logs";
	private KurrentDBOptions _options;

	public LogsEndpointPlugin () {
		_logger.Information("LogsEndpointPlugin is loaded");
	}

	// ISubsystemsPlugin
	public IReadOnlyList<ISubsystem> GetSubsystems() => new[] { this };

	// ISubsystem
	public void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		_options = configuration.GetSection("KurrentDB").Get<KurrentDBOptions>();
	}

	void DisableEndpoint(Exception ex) {
		_logger.Information("LogsEndpoint: Failed to get license, endpoint will not be available.");
		Enabled = false;
	}

	public void ConfigureApplication(IApplicationBuilder builder, IConfiguration configuration) {
		var diagnosticListener = new DiagnosticListener(DiagnosticsName);
		var value = new PluginDiagnosticsData {
			Source = DiagnosticsName,
			Data = new() { ["enabled"] = Enabled },
			CollectionMode = PluginDiagnosticsDataCollectionMode.Partial
		};
		diagnosticListener.Write(nameof(PluginDiagnosticsData), value);

		var licenseService = builder.ApplicationServices.GetService<ILicenseService>();
		var logger = builder.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());

		_ = LicenseMonitor.MonitorAsync(
			featureName: Name,
			requiredEntitlements: ["LOGS_ENDPOINT"],
			licenseService: licenseService,
			onLicenseException: DisableEndpoint,
			logger: logger,
			licensePublicKey: LicensePublicKey);

		if (_options == null) {
			_logger.Error("LogsEndpoint: Failed to get configuration, endpoint will not be available!");
			return;
		}

		if (_options.Log == null) {
			_logger.Error("LogsEndpoint: Failed to get the configured directory for log files, endpoint will not be available!");
			return;
		}

		if (_options.NodeIp == null) {
			_logger.Error("LogsEndpoint: Failed to get the configured node IP, endpoint will not be available!");
			return;
		}

		if (_options.NodePort == null) {
			_logger.Error("LogsEndpoint: Failed to get the configured node port, endpoint will not be available!");
			return;
		}

		var logsDir = Path.Combine(_options.Log, $"{_options.NodeIp}-{_options.NodePort}-cluster-node");
		logsDir = Path.GetFullPath(logsDir);
		if (!Directory.Exists(logsDir)) {
			_logger.Error("LogsEndpoint: Failed to find the log files directory at {logsDir}, endpoint will not be available", logsDir);
			return;
		}

		ConfigureEndpoint(builder, logsDir);
	}

	private void ConfigureEndpoint(IApplicationBuilder builder, string logsDir) {
		_logger.Information("LogsEndpoint: Serving logs from {dir} at endpoint {endpoint}", logsDir, EndpointPath);

		builder.Map(EndpointPath, b => b
			.Use(async (context, next) => {
				if (!Enabled) {
					context.Response.StatusCode = (int)HttpStatusCode.NotFound;
				} else if (context.User.IsInRole("$ops") || context.User.IsInRole("$admins")) {
					await next(context);
				} else {
					context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
				}
			})
			.UseFileServer(new FileServerOptions {
				EnableDirectoryBrowsing = true,
				FileProvider = new PhysicalFileProvider(logsDir),
				DirectoryBrowserOptions = {
					Formatter = new JsonDirectoryFormatter(),
					RedirectToAppendTrailingSlash = false,
				},
				StaticFileOptions = {
					ContentTypeProvider = JsonOnlyContentTypeProvider(),
				}
			}));

		static FileExtensionContentTypeProvider JsonOnlyContentTypeProvider() {
			var c = new FileExtensionContentTypeProvider();
			c.Mappings.Clear();
			c.Mappings.Add(".json", "application/json");
			return c;
		}
	}

	public Task Start() => Task.CompletedTask;

	public Task Stop() => Task.CompletedTask;

	class KurrentDBOptions {
		public string Log { get; set; }
		public string NodeIp { get; set; }
		public string NodePort { get; set; }
	}
}
