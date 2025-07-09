// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace EventStore.Plugins.Tests.Diagnostics;

public class PluginMetricsTests {
	[Fact]
	public void can_receive_metrics_from_plugin() {
		// Arrange
		IPlugableComponent plugin = new AdamSmasherPlugin(diagnosticsTags: new KeyValuePair<string, object?>("test_name", "can_receive_metrics_from_plugin"));

		var builder = WebApplication.CreateBuilder();

		plugin.ConfigureServices(builder.Services, builder.Configuration);

		using var app = builder.Build();

		plugin.ConfigureApplication(app, builder.Configuration);

		using var collector = new MetricCollector<int>(
			app.Services.GetRequiredService<IMeterFactory>(),
			plugin.DiagnosticsName,
			((AdamSmasherPlugin)plugin).TestCounter.Name
		);

		// Act
		((AdamSmasherPlugin)plugin).TestCounter.Add(1, plugin.DiagnosticsTags); // we also need to add then here? ffs... they should propagate from the meter...

		// Assert
		var collectedMeasurement = collector.GetMeasurementSnapshot().Should().ContainSingle().Which;

		collectedMeasurement.Value.Should().Be(1);

		collectedMeasurement.Tags.Should().BeEquivalentTo(plugin.DiagnosticsTags);
	}

	class AdamSmasherPlugin(params KeyValuePair<string, object?>[] diagnosticsTags) : Plugin(diagnosticsTags: diagnosticsTags) {
		public Counter<int> TestCounter { get; private set; } = null!;

		public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
			var meterFactory = app.ApplicationServices.GetRequiredService<IMeterFactory>();

			var meter = meterFactory.Create(DiagnosticsName, Version, DiagnosticsTags);

			TestCounter = meter.CreateCounter<int>(
				"plugin_test_calls",
				"int",
				"just to test the counter",
				DiagnosticsTags
			);
		}
	}
}
