// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using EventStore.Plugins.Diagnostics;
using EventStore.Plugins.Licensing;
using EventStore.Plugins.TestHelpers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EventStore.OtlpExporterPlugin.Tests;

[Collection("OtlpSequentialTests")] // so that the PluginDiagnosticsDataCollectors do not conflict
public class OtlpExporterPluginTests {
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void can_collect_telemetry(bool enabled) {
		// given
		using var sut = new OtlpExporterPlugin();
		using var collector = PluginDiagnosticsDataCollector.Start(sut.DiagnosticsName);

		IConfigurationBuilder configBuilder = new ConfigurationBuilder();

		if (enabled)
			configBuilder = configBuilder.AddInMemoryCollection(new Dictionary<string, string?> {
				{$"{KurrentConfigurationConstants.Prefix}:OpenTelemetry:Otlp:Endpoint", "http://localhost:1234"},
			});

		var config = configBuilder.Build();

		var builder = WebApplication.CreateBuilder();
		builder.Services.AddSingleton<ILicenseService>(new Fixtures.FakeLicenseService());

		// when
		((IPlugableComponent)sut).ConfigureServices(
			builder.Services,
			config);

		var app = builder.Build();
		((IPlugableComponent)sut).ConfigureApplication(app, config);

		// then
		collector.CollectedEvents(sut.DiagnosticsName).Should().ContainSingle().Which
			.Data["enabled"].Should().Be(enabled);
	}

	[Theory]
	[InlineData(true, true, "OTLP_EXPORTER", false)]
	[InlineData(true, false, "OTLP_EXPORTER", true)]
	[InlineData(false, true, "OTLP_EXPORTER", false)]
	[InlineData(false, false, "OTLP_EXPORTER", false)]
	[InlineData(true, true, "NONE", true)]
	[InlineData(true, false, "NONE", true)]
	[InlineData(false, true, "NONE", false)]
	[InlineData(false, false, "NONE", false)]
	public void respects_license(bool enabled, bool licensePresent, string entitlement, bool expectedException) {
		// given
		using var sut = new OtlpExporterPlugin();

		IConfigurationBuilder configBuilder = new ConfigurationBuilder();

		if (enabled)
			configBuilder = configBuilder.AddInMemoryCollection(new Dictionary<string, string?> {
				{$"{KurrentConfigurationConstants.Prefix}:OpenTelemetry:Otlp:Endpoint", "http://localhost:1234"},
			});

		var config = configBuilder.Build();

		var builder = WebApplication.CreateBuilder();

		var licenseService = new Fixtures.FakeLicenseService(licensePresent, entitlement);
		builder.Services.AddSingleton<ILicenseService>(licenseService);

		((IPlugableComponent)sut).ConfigureServices(
			builder.Services,
			config);

		var app = builder.Build();

		// when
		((IPlugableComponent)sut).ConfigureApplication(app, config);

		// then
		if (expectedException) {
			Assert.NotNull(licenseService.RejectionException);
		} else {
			Assert.Null(licenseService.RejectionException);
		}
	}
}
