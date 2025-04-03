// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using Serilog;

namespace EventStore.OtlpExporterPlugin;

public class OtlpExporterPlugin : SubsystemsPlugin {
	public const string KurrentConfigurationPrefix = "KurrentDB";
	private static readonly ILogger _staticLogger = Log.ForContext<OtlpExporterPlugin>();
	private readonly ILogger _logger;

	public OtlpExporterPlugin() : this(_staticLogger) {
	}

	public OtlpExporterPlugin(ILogger logger)
		: base(
			requiredEntitlements: ["OTLP_EXPORTER"]) {

		_logger = logger;
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
						_logger.Warning(
							$"OtlpExporter: {KurrentConfigurationPrefix}:OpenTelemetry:Metrics:PeriodicExportingMetricReaderOptions:ExportIntervalMilliseconds " +
							$"({{exportInterval}} ms) does not match {KurrentConfigurationPrefix}:Metrics:ExpectedScrapeIntervalSeconds " +
							"({scrapeInterval} s). Periodic maximum metrics may not be reported correctly.",
							periodicOptions.ExportIntervalMilliseconds, scrapeIntervalSeconds);
					}

					_logger.Information("OtlpExporter: Exporting metrics to {endpoint} every {interval:N1} seconds",
						exporterOptions.Endpoint,
						periodicOptions.ExportIntervalMilliseconds / 1000.0);
				}));
	}
}
