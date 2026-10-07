// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scrutor;

namespace KurrentDB;

/// <summary>
/// The order in which a web application has to be wired around a node.
/// </summary>
/// <remarks>
/// Every host that runs a <see cref="ClusterVNodeHostedService"/> wires it up the same way — the server
/// executable, the embedded server and the test harnesses — and the ordering is not something the types
/// enforce. Keeping it in one place is what stops the hosts drifting apart on it, and what gives the
/// reasons somewhere to live. What a host puts around the node, such as the server executable's UI, stays
/// with that host.
/// </remarks>
public static class NodeWebApplication {
	/// <summary>
	/// Registers the node's services and the hosted service that runs it. Call it before the host adds
	/// services of its own, so that the host can replace anything the node registered.
	/// </summary>
	public static void ConfigureServices(IServiceCollection services, ClusterVNodeHostedService hostedService) {
		// The node goes first so that the subsystems can resolve their dependencies out of the DI in
		// Configure() before being started. The hosted service goes in after it, so that it and the
		// subsystems are started once configuration has finished.
		hostedService.Node.Startup.ConfigureServices(services);
		services.AddSingleton<IHostedService>(hostedService);
	}

	/// <summary>
	/// Logs when each hosted service starts and stops, and how long it took, so that a slow or stalling
	/// one stands out.
	/// </summary>
	/// <remarks>
	/// Call it once every hosted service has been registered, the host's own included: it decorates what
	/// is already there, so anything registered afterwards goes unreported.
	/// </remarks>
	public static void LogHostedServiceLifecycle(IServiceCollection services) =>
		services.Decorate<IHostedService, HostedServiceLifecycleDecorator>();

	/// <summary>
	/// Wires the node's request pipeline. Call it on the built application.
	/// </summary>
	/// <remarks>
	/// This sets up routing and the endpoints, so a host with middleware that has to see a request before
	/// the node's endpoints claim it must add that middleware before calling this.
	/// </remarks>
	public static void Configure(WebApplication app, ClusterVNodeHostedService hostedService) =>
		hostedService.Node.Startup.Configure(app);
}
