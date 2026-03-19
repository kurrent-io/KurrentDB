// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Logging;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Exporter;
using Serilog;

namespace KurrentDB.Common.Tests.OpenTelemetry;

public class OpenTelemetryLoggerTests {
	[Fact]
	public void shared_config_used_when_no_per_signal_section() {
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KurrentDB:OpenTelemetry:Otlp:Endpoint", "http://shared:4317" },
				{ "KurrentDB:OpenTelemetry:Otlp:Headers", "key=shared" },
				{ "KurrentDB:OpenTelemetry:Logs:Enabled", "true" },
			})
			.Build();

		new LoggerConfiguration().AddOpenTelemetryLogger(config, "test-node");

		Assert.NotNull(OpenTelemetryLogger.OtlpOptions);
		Assert.Equal(new Uri("http://shared:4317"), OpenTelemetryLogger.OtlpOptions.Endpoint);
		Assert.Equal("key=shared", OpenTelemetryLogger.OtlpOptions.Headers);
		Assert.Equal(OtlpExportProtocol.Grpc, OpenTelemetryLogger.OtlpOptions.Protocol); // default
	}

	[Fact]
	public void per_signal_endpoint_overrides_shared() {
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KurrentDB:OpenTelemetry:Otlp:Endpoint", "http://shared:4317" },
				{ "KurrentDB:OpenTelemetry:Otlp:Headers", "key=shared" },
				{ "KurrentDB:OpenTelemetry:Logs:Otlp:Endpoint", "http://logs:4317" },
				{ "KurrentDB:OpenTelemetry:Logs:Enabled", "true" },
			})
			.Build();

		new LoggerConfiguration().AddOpenTelemetryLogger(config, "test-node");

		Assert.NotNull(OpenTelemetryLogger.OtlpOptions);
		Assert.Equal(new Uri("http://logs:4317"), OpenTelemetryLogger.OtlpOptions.Endpoint);
		Assert.Equal("key=shared", OpenTelemetryLogger.OtlpOptions.Headers); // inherited from shared
		Assert.Equal(OtlpExportProtocol.Grpc, OpenTelemetryLogger.OtlpOptions.Protocol); // inherited from shared
	}

	[Fact]
	public void per_signal_headers_override_shared() {
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KurrentDB:OpenTelemetry:Otlp:Endpoint", "http://shared:4317" },
				{ "KurrentDB:OpenTelemetry:Otlp:Headers", "key=shared" },
				{ "KurrentDB:OpenTelemetry:Logs:Otlp:Headers", "key=logs-only" },
				{ "KurrentDB:OpenTelemetry:Logs:Enabled", "true" },
			})
			.Build();

		new LoggerConfiguration().AddOpenTelemetryLogger(config, "test-node");

		Assert.NotNull(OpenTelemetryLogger.OtlpOptions);
		Assert.Equal(new Uri("http://shared:4317"), OpenTelemetryLogger.OtlpOptions.Endpoint); // inherited from shared
		Assert.Equal("key=logs-only", OpenTelemetryLogger.OtlpOptions.Headers);
	}

	[Fact]
	public void per_signal_overrides_all_properties() {
		var config = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> {
				{ "KurrentDB:OpenTelemetry:Otlp:Endpoint", "http://shared:4317" },
				{ "KurrentDB:OpenTelemetry:Otlp:Headers", "key=shared" },
				{ "KurrentDB:OpenTelemetry:Logs:Otlp:Endpoint", "http://logs:4317" },
				{ "KurrentDB:OpenTelemetry:Logs:Otlp:Headers", "key=logs" },
				{ "KurrentDB:OpenTelemetry:Logs:Otlp:Protocol", "HttpProtobuf" },
				{ "KurrentDB:OpenTelemetry:Logs:Enabled", "true" },
			})
			.Build();

		new LoggerConfiguration().AddOpenTelemetryLogger(config, "test-node");

		Assert.NotNull(OpenTelemetryLogger.OtlpOptions);
		Assert.Equal(new Uri("http://logs:4317"), OpenTelemetryLogger.OtlpOptions.Endpoint);
		Assert.Equal("key=logs", OpenTelemetryLogger.OtlpOptions.Headers);
		Assert.Equal(OtlpExportProtocol.HttpProtobuf, OpenTelemetryLogger.OtlpOptions.Protocol);
	}
}
