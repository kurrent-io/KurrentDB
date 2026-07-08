// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using KurrentDB.Core;
using KurrentDB.Core.Configuration.Sources;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Plugins.LogsQuery;

public class LogsQueryPlugin() : SubsystemsPlugin(name: "LogsQuery", requiredEntitlements: [LogsQueryLicense.Entitlement]) {
	readonly LogsQueryLicense _license = new();

	public override (bool Enabled, string EnableInstructions) IsEnabled(IConfiguration configuration) {
		var enabled = configuration.GetValue($"{KurrentConfigurationKeys.Prefix}:LogsQuery:Enabled", false);
		return (enabled, "Set KurrentDB__LogsQuery__Enabled to true to enable the logs query API.");
	}

	// Keep the node running when the entitlement is missing; individual RPCs
	// answer FailedPrecondition via the license flag instead.
	protected override void OnLicenseException(Exception ex, Action<Exception> shutdown) =>
		_license.Disable();

	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		services.AddSingleton(_license);
		services.AddSingleton(sp => {
			var options = sp.GetRequiredService<ClusterVNodeOptions>();
			var logsDir = Path.GetFullPath(Path.Combine(options.Logging.Log, options.GetComponentName()));
			return LogsQueryDatabase.Create(logsDir);
		});
		services.AddGrpc();
	}

	public override void ConfigureApplication(IApplicationBuilder app, IConfiguration configuration) {
		app.UseEndpoints(endpoints => endpoints.MapGrpcService<LogsQueryService>());
	}
}
