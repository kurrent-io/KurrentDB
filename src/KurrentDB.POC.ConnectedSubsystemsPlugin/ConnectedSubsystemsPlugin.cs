// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins;
using KurrentDB.POC.IO.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.POC.ConnectedSubsystemsPlugin;

// Provides the in-process IClient and IOperationsClient used by connected subsystems (e.g. AutoScavenge)
public class ConnectedSubsystemsPlugin : SubsystemsPlugin {
	public ConnectedSubsystemsPlugin() : base(version: "0.0.5", name: "ConnectedSubsystems") {
	}

	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		services.AddSingleton<IClient, InternalClient>();
		services.AddSingleton<IOperationsClient, InternalOperationsClient>();
	}
}
