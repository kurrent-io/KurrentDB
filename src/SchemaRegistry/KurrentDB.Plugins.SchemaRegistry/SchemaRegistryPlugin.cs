// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics.CodeAnalysis;
using DotNext;
using EventStore.Plugins;
using KurrentDB.SchemaRegistry;
using KurrentDB.Surge;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Plugins.SchemaRegistry;

[UsedImplicitly]
public class SchemaRegistryPlugin : SubsystemsPlugin {
	private const string FeatureName = $"{IPlugableComponent.FeatureNamePrefix}.SchemaRegistry";

	[FeatureSwitchDefinition(FeatureName)]
	public static bool IsAllowed { get; } = AppContext.IsFeatureSupported(FeatureName);

	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		services
			.AddSurgeSystemComponents()
			.AddSchemaRegistryService();
	}

	public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
		app.UseSchemaRegistryService();
	}

	public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
		var enabled = configuration.GetValue(
			$"KurrentDB:{Name}:Enabled",
			configuration.GetValue($"{Name}:Enabled",
				configuration.GetValue("Enabled", true)
			)
		);

		return (enabled, "Please check the documentation for instructions on how to enable the plugin.");
	}
}
