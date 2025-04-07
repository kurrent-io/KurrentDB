// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Threading.Tasks;
using EventStore.Core;
using EventStore.Core.Certificates;
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
		var options = configuration.GetSection("EventStore:TcpUnitTestPlugin").Get<TcpApiTestOptions>() ?? new();

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
