// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scrutor;

namespace KurrentDB;

/// <summary>
/// The order in which a web application has to be wired around a node, which every host does the same way:
/// the server executable, the embedded server and the test harnesses.
/// </summary>
public static class NodeWebApplication {
	public static void ConfigureServices(IServiceCollection services, ClusterVNodeHostedService hostedService) {
		hostedService.Node.Startup.ConfigureServices(services);
		// Order is important, configure IHostedService after the WebHost to make the sure
		// ClusterVNodeHostedService and the subsystems are started after configuration is finished.
		// Allows the subsystems to resolve dependencies out of the DI in Configure() before being started.
		// Later it may be possible to use constructor injection instead if it fits with the bootstrapping strategy.
		services.AddSingleton<IHostedService>(hostedService);
	}

	// call once every hosted service has been registered, the host's own included: it decorates what is
	// already there.
	public static void LogHostedServiceLifecycle(IServiceCollection services) =>
		services.Decorate<IHostedService, HostedServiceLifecycleDecorator>();

	public static void Configure(WebApplication app, ClusterVNodeHostedService hostedService) =>
		hostedService.Node.Startup.Configure(app);
}
