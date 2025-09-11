// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using KurrentDB.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using Serilog;
using ILogger = Serilog.ILogger;

namespace KurrentDB.OtlpExporterPlugin;

public class OtlpExporterPlugin(ILogger logger) : SubsystemsPlugin(requiredEntitlements: ["OTLP_EXPORTER"]) {
	private const string KurrentConfigurationPrefix = "KurrentDB";
	private static readonly ILogger _staticLogger = Log.ForContext<OtlpExporterPlugin>();

	public OtlpExporterPlugin() : this(_staticLogger) {
	}

	public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
		var enabled = MetricsExportEnabled(configuration);
		return (enabled, $"No {KurrentConfigurationPrefix}:OpenTelemetry:Otlp configuration found. Not exporting metrics and logs.");
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

		services
			.Configure<OtlpExporterOptions>(configuration.GetSection($"{KurrentConfigurationPrefix}:OpenTelemetry:Otlp"))
			.Configure<MetricReaderOptions>(configuration.GetSection($"{KurrentConfigurationPrefix}:OpenTelemetry:Metrics"))
			.AddOpenTelemetry()
			.WithMetrics(configure => configure
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
	}

	public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
		base.ConfigureApplication(app, configuration);
		app.ApplicationServices.GetService<OpenTelemetryLogger>();
	}

	private static bool MetricsExportEnabled(IConfiguration configuration)
		=> configuration.GetSection($"{KurrentConfigurationPrefix}:OpenTelemetry:Otlp").Exists();
}
