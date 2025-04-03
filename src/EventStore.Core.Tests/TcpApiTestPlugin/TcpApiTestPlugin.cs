// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Core;
using EventStore.Core.Certificates;
using EventStore.Core.Configuration.Sources;
using EventStore.Core.Services;
using EventStore.Plugins;
using EventStore.Plugins.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EventStore.TcpUnitTestPlugin;

public class TcpApiTestPlugin() : SubsystemsPlugin(name: "TcpTestApi") {
	static readonly ILogger Logger = Log.ForContext<TcpApiTestPlugin>();

	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		var options = configuration.GetSection($"{KurrentConfigurationKeys.Prefix}:TcpUnitTestPlugin").Get<TcpApiTestOptions>() ?? new();

		services.AddHostedService<PublicTcpApiTestService>(serviceProvider => {
			var components = serviceProvider.GetRequiredService<StandardComponents>();
			var authGateway = serviceProvider.GetRequiredService<AuthorizationGateway>();
			var authProvider = serviceProvider.GetRequiredService<IAuthenticationProvider>();

			return options.Insecure
				? PublicTcpApiTestService.Insecure(options, authProvider, authGateway, components)
				: PublicTcpApiTestService.Secure(options, authProvider, authGateway, components, serviceProvider.GetService<CertificateProvider>());
		});
	}

	public override Task Start() {
		Logger.Debug("{Name}-{Version} test plugin is loaded", Name, Version);
		return Task.CompletedTask;
	}
}
