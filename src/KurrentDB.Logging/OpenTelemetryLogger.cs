// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Common.Utils;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;

namespace KurrentDB.Logging;

public sealed class OpenTelemetryLogger : ILogEventSink, IDisposable {
	Logger? _log;
	ILogEventSink? _sink;

	const string KurrentConfigurationPrefix = "KurrentDB";

	public OpenTelemetryLogger(IConfiguration configuration, string componentName) {
		var logExporterConfig = configuration.GetSection($"{KurrentConfigurationPrefix}:OpenTelemetry:Logging").Get<LogRecordExportProcessorOptions>()!;
		var otlpExporterConfig = configuration.GetSection($"{KurrentConfigurationPrefix}:OpenTelemetry:Otlp").Get<OtlpExporterOptions>()!;

		if (!string.IsNullOrWhiteSpace(otlpExporterConfig.Headers)) {
			// Let Serilog parse the headers string into a dictionary instead of trying to replicate their logic
			Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS", otlpExporterConfig.Headers);
		}

		_log = new LoggerConfiguration()
			.WriteTo.OpenTelemetry(options => {
				options.ResourceAttributes = new Dictionary<string, object> {
					["service.name"] = "KurrentDB",
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
			})
			.CreateLogger();
		_sink = _log;
	}

	public void Disable() {
		Dispose();
		_sink = null;
		_log = null;
	}

	public void Emit(LogEvent logEvent) {
		_sink?.Emit(logEvent);
	}

	public void Dispose() {
		_log?.Dispose();
	}
}
