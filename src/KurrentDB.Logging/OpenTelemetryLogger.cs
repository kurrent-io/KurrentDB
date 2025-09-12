// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Common.Configuration;
using KurrentDB.Common.Utils;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using Serilog;
using Serilog.Filters;
using Serilog.Sinks.OpenTelemetry;

namespace KurrentDB.Logging;

public static class OpenTelemetryLogger {
	public static LoggerConfiguration AddOpenTelemetryLogger(this LoggerConfiguration config, IConfiguration configuration, string componentName) {
		if (!configuration.OtlpLogsEnabled())
			return config;

		var logExporterConfig = configuration.GetSection(ConfigConstants.OtlpLogsPrefix).Get<LogRecordExportProcessorOptions>() ?? new();
		var otlpExporterConfig = configuration.GetSection(ConfigConstants.OtlpConfigPrefix).Get<OtlpExporterOptions>() ?? new();
		var metricsConfig = MetricsConfiguration.Get(configuration);

		return config
			.Filter.ByExcluding(Matching.FromSource("REGULAR-STATS-LOGGER"))
			.WriteTo.OpenTelemetry(options => {
				options.ResourceAttributes = new Dictionary<string, object> {
					["service.name"] = metricsConfig.ServiceName,
					["service.instance.id"] = componentName,
					["service.version"] = VersionInfo.Version
				};
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
			}, getConfigurationVariable: name => name switch {
				// Let Serilog parse the headers string into a dictionary instead of trying to replicate their logic
				"OTEL_EXPORTER_OTLP_HEADERS" => otlpExporterConfig.Headers,
				_ => Environment.GetEnvironmentVariable(name),
			});
	}
}
