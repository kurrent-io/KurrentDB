// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Plugins;
using KurrentDB.Common.Configuration;
using KurrentDB.Common.Log;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using Serilog;
using Serilog.Core;
using Serilog.Sinks.OpenTelemetry;
using ILogger = Serilog.ILogger;

namespace KurrentDB.OtlpExporterPlugin;

public class OtlpExporterPlugin(ILogger logger) : SubsystemsPlugin(requiredEntitlements: ["OTLP_EXPORTER"]) {
	public const string KurrentConfigurationPrefix = "KurrentDB";
	private static readonly ILogger _staticLogger = Log.ForContext<OtlpExporterPlugin>();

	public OtlpExporterPlugin() : this(_staticLogger) {
	}

	public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
		var enabled = configuration.GetSection($"{KurrentConfigurationPrefix}:OpenTelemetry:Otlp").Exists();
		return (enabled, $"No {KurrentConfigurationPrefix}:OpenTelemetry:Otlp configuration found. Not exporting metrics.");
	}

	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		// there are two related settings
		//
		// "KurrentDB:Metrics:ExpectedScrapeIntervalSeconds"
		//    this is how often (seconds) the metrics themselves expect to be scraped (they will hold on to
		//    periodic maximum values long enough to ensure they are captured
		//
		// "KurrentDB:OpenTelemetry:Metrics:PeriodicExportingMetricReaderOptions:ExportIntervalMilliseconds"
		//    this is how often (milliseconds) the metrics are exported from periodic exporters like this one
		//
		// we want to respect the OpenTelemetry setting, but the behaviour will make most sense if the two
		// settings are in agreement with each other. therefore:
		//    if ExportInterval is not set, derive it from ExpectedScrapeInterval
		//    if ExportInterval is set, use it, but warn if it is out of sync with ExpectedScrapeInterval
		//
		// later it may be possible to use ExportInterval to drive ExpectedScrapeInterval in the main server,
		// this would be a breaking change and we'd probably do it at the same time as the breaking change of
		// removing the special handling of metricsconfig.json where ExpectedScrapeInterval is defined.

		var scrapeIntervalSeconds = configuration.GetValue<int>($"{KurrentConfigurationPrefix}:Metrics:ExpectedScrapeIntervalSeconds");
		var logExportEnabled = configuration.GetValue<bool>($"{KurrentConfigurationPrefix}:OpenTelemetry:Logging:Enabled");

		services
			.Configure<OtlpExporterOptions>(configuration.GetSection($"{KurrentConfigurationPrefix}:OpenTelemetry:Otlp"))
			.Configure<MetricReaderOptions>(configuration.GetSection($"{KurrentConfigurationPrefix}:OpenTelemetry:Metrics"))
			.AddOpenTelemetry()
			.WithMetrics(o => o
				.AddOtlpExporter((exporterOptions, metricReaderOptions) => {
					var periodicOptions = metricReaderOptions.PeriodicExportingMetricReaderOptions;
					if (periodicOptions.ExportIntervalMilliseconds is null) {
						periodicOptions.ExportIntervalMilliseconds = scrapeIntervalSeconds * 1000;
					} else if (periodicOptions.ExportIntervalMilliseconds != scrapeIntervalSeconds * 1000) {
						logger.Warning(
							$"OtlpExporter: {KurrentConfigurationPrefix}:OpenTelemetry:Metrics:PeriodicExportingMetricReaderOptions:ExportIntervalMilliseconds " +
							$"({{exportInterval}} ms) does not match {KurrentConfigurationPrefix}:Metrics:ExpectedScrapeIntervalSeconds " +
							"({scrapeInterval} s). Periodic maximum metrics may not be reported correctly.",
							periodicOptions.ExportIntervalMilliseconds, scrapeIntervalSeconds);
					}

					logger.Information("OtlpExporter: Exporting metrics to {endpoint} every {interval:N1} seconds",
						exporterOptions.Endpoint,
						periodicOptions.ExportIntervalMilliseconds / 1000.0);
				}));

		if (!logExportEnabled) return;

		var logExporterConfig = configuration.GetSection($"{KurrentConfigurationPrefix}:OpenTelemetry:Logging").Get<LogRecordExportProcessorOptions>()!;
		var otlpExporterConfig = configuration.GetSection($"{KurrentConfigurationPrefix}:OpenTelemetry:Otlp").Get<OtlpExporterOptions>()!;

		if (!string.IsNullOrWhiteSpace(otlpExporterConfig.Headers)) {
			// Let Serilog parse the headers string into a dictionary instead of trying to replicate their logic
			Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS", otlpExporterConfig.Headers);
		}

		var loggingOptions = configuration.BindOptions<LoggingOptions>();
		Log.Logger = KurrentLoggerConfiguration.CreateLoggerConfiguration(loggingOptions, configuration["Temp:ComponentName"]!)
			// Use the existing logger as a sink
			.WriteTo.OpenTelemetry(options => {
				options.Endpoint = otlpExporterConfig.Endpoint.AbsoluteUri;
				options.Protocol = otlpExporterConfig.Protocol switch {
					OtlpExportProtocol.Grpc => OtlpProtocol.Grpc,
					OtlpExportProtocol.HttpProtobuf => OtlpProtocol.HttpProtobuf,
					_ => throw new ArgumentOutOfRangeException(">" + otlpExporterConfig.Protocol + "<", "Invalid protocol for OTLP exporter.")
				};
				options.BatchingOptions.BatchSizeLimit = logExporterConfig.BatchExportProcessorOptions.MaxExportBatchSize;
				options.BatchingOptions.BufferingTimeLimit = TimeSpan.FromMilliseconds(logExporterConfig.BatchExportProcessorOptions.ScheduledDelayMilliseconds);
				options.BatchingOptions.QueueLimit = logExporterConfig.BatchExportProcessorOptions.MaxQueueSize;
				options.BatchingOptions.RetryTimeLimit = TimeSpan.FromMilliseconds(logExporterConfig.BatchExportProcessorOptions.ExporterTimeoutMilliseconds);
			})
			.CreateLogger();
		logger.Information("OtlpExporter: Exporting logs to {endpoint}", otlpExporterConfig.Endpoint);
	}
}
