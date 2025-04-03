// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using EventStore.Plugins;
using EventStore.Plugins.Diagnostics;
using EventStore.Plugins.Licensing;
using EventStore.Plugins.TestHelpers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Resources;
using Xunit;

namespace EventStore.OtlpExporterPlugin.Tests;

[Collection("OtlpSequentialTests")] // so that the PluginDiagnosticsDataCollectors do not conflict
public class OtlpExporterTests {
	private const string Endpoint = "http://localhost:6506";
	private readonly FakeLogger _logger = new();

	[Fact]
	public void has_parameterless_constructor() {
		// needed for all plugins
		using var _ = Activator.CreateInstance<OtlpExporterPlugin>();
	}

	[Fact]
	public async Task disabled_when_no_config() {
		using var collector = PluginDiagnosticsDataCollector.Start("OtlpExporter");
		await using var _ = await CreateServer(new(), []);
		collector.CollectedEvents("OtlpExporter").Should().ContainSingle().Which
			.Data["enabled"].Should().Be(false);
	}

	[Fact]
	public async Task respects_expected_scrape_interval() {
		await using var _ = await CreateServer(new(), new Dictionary<string, string?> {
			{ $"{KurrentConfigurationConstants.Prefix}:OpenTelemetry:Otlp:Endpoint", Endpoint },
			{ $"{KurrentConfigurationConstants.Prefix}:Metrics:ExpectedScrapeIntervalSeconds", "5" },
		});
		var log = Assert.Single(_logger.LogMessages);
		Assert.Equal("OtlpExporter: Exporting metrics to http://localhost:6506/ every 5.0 seconds", log.RenderMessage());
	}

	[Fact]
	public async Task export_interval_superceeds_expected_scrape_interval() {
		await using var _ = await CreateServer(new(), new Dictionary<string, string?> {
			{ $"{KurrentConfigurationConstants.Prefix}:OpenTelemetry:Otlp:Endpoint", Endpoint },
			{ $"{KurrentConfigurationConstants.Prefix}:OpenTelemetry:Metrics:PeriodicExportingMetricReaderOptions:ExportIntervalMilliseconds", "5000" },
			{ $"{KurrentConfigurationConstants.Prefix}:Metrics:ExpectedScrapeIntervalSeconds", "4" },
		});
		Assert.Collection(_logger.LogMessages,
			log => Assert.Equal(
				$"OtlpExporter: {KurrentConfigurationConstants.Prefix}:OpenTelemetry:Metrics:PeriodicExportingMetricReaderOptions:ExportIntervalMilliseconds " +
				$"(5000 ms) does not match {KurrentConfigurationConstants.Prefix}:Metrics:ExpectedScrapeIntervalSeconds " +
				"(4 s). Periodic maximum metrics may not be reported correctly.",
				log.RenderMessage()),
			log => Assert.Equal("OtlpExporter: Exporting metrics to http://localhost:6506/ every 5.0 seconds", log.RenderMessage()));
	}

	[Fact]
	public async Task does_not_warn_when_settings_agree() {
		await using var _ = await CreateServer(new(), new Dictionary<string, string?> {
			{ $"{KurrentConfigurationConstants.Prefix}:OpenTelemetry:Otlp:Endpoint", Endpoint },
			{ $"{KurrentConfigurationConstants.Prefix}:OpenTelemetry:Metrics:PeriodicExportingMetricReaderOptions:ExportIntervalMilliseconds", "5000" },
			{ $"{KurrentConfigurationConstants.Prefix}:Metrics:ExpectedScrapeIntervalSeconds", "5" },
		});
		var log = Assert.Single(_logger.LogMessages);
		Assert.Equal("OtlpExporter: Exporting metrics to http://localhost:6506/ every 5.0 seconds", log.RenderMessage());
	}

	[Fact]
	public async Task exports_metrics() {
		var meter = new Meter("EventStore.TestMeter", version: "1.0.0");
		meter.CreateObservableCounter("test-counter", () => 7);

		var tcs = new TaskCompletionSource<ExportMetricsServiceRequest>();

		await using var _ = await CreateServer(tcs, new Dictionary<string, string?> {
			{ $"{KurrentConfigurationConstants.Prefix}:OpenTelemetry:Otlp:Endpoint", Endpoint },
			{ $"{KurrentConfigurationConstants.Prefix}:Metrics:ExpectedScrapeIntervalSeconds", "1" }
		});

		var log = Assert.Single(_logger.LogMessages);
		Assert.Equal("OtlpExporter: Exporting metrics to http://localhost:6506/ every 1.0 seconds", log.RenderMessage());

		if (await Task.WhenAny(tcs.Task, Task.Delay(15_000)) != tcs.Task)
			throw new Exception("timed out");

		var exportReceived = await tcs.Task;

		var resourceMetric = Assert.Single(exportReceived.ResourceMetrics);
		var scopeMetric = Assert.Single(resourceMetric.ScopeMetrics);
		var metric = Assert.Single(scopeMetric.Metrics);
		Assert.Equal("test-counter", metric.Name);
		var dataPoint = Assert.Single(metric.Sum.DataPoints);
		Assert.Equal(7, dataPoint.AsInt);
	}

	// server hosts the sut, and doubles up as a fake collector for exported metrics
	private async Task<IAsyncDisposable> CreateServer(
		TaskCompletionSource<ExportMetricsServiceRequest> receiveExport,
		Dictionary<string, string?> configuration) {

		var builder = WebApplication.CreateBuilder();
		builder.Configuration.AddInMemoryCollection(configuration);
		builder.WebHost
			.UseUrls(Endpoint)
			.ConfigureKestrel(x => x
				.ConfigureEndpointDefaults(y => y.Protocols = HttpProtocols.Http2));
		builder.Services
			.AddSingleton<ILicenseService>(new Fixtures.FakeLicenseService())
			.AddSingleton(new FakeCollector(receiveExport))
			.AddGrpc().Services
			.AddOpenTelemetry()
				.WithMetrics(meterOptions => meterOptions
					.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("eventstore"))
					.AddMeter("EventStore.TestMeter"));

		var sut = new OtlpExporterPlugin(_logger);
		((IPlugableComponent)sut).ConfigureServices(builder.Services, builder.Configuration);

		var app = builder.Build();

		app.MapGrpcService<FakeCollector>();
		((IPlugableComponent)sut).ConfigureApplication(app, builder.Configuration);

		await app.StartAsync();
		return new AsyncDisposables(
			new AsyncDisposable(sut),
			app);
	}

	class AsyncDisposable(IDisposable disposable) : IAsyncDisposable {
		public ValueTask DisposeAsync() {
			disposable.Dispose();
			return ValueTask.CompletedTask;
		}
	}

	class AsyncDisposables(params IAsyncDisposable[] disposables) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			foreach (var disposable in disposables) {
				await disposable.DisposeAsync();
			}
		}
	}
}
