// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.IO;
using EventStore.Common.Utils;
using EventStore.PluginHosting;
using EventStore.Plugins;
using EventStore.Plugins.Subsystems;
using EventStore.POC.IO.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EventStore.POC.ConnectedSubsystemsPlugin;

public class ConnectedSubsystemsPlugin : SubsystemsPlugin {
	private static readonly ILogger Log = Serilog.Log.ForContext<ConnectedSubsystemsPlugin>();

	public ConnectedSubsystemsPlugin() : base(version: "0.0.5", name: "ConnectedSubsystems") {
	}

	public override IReadOnlyList<ISubsystem> GetSubsystems() {
		var conntectedSubsystemsDir = new DirectoryInfo(Locations.PluginsDirectory);
		var pluginLoader = new PluginLoader(conntectedSubsystemsDir, typeof(IClient).Assembly);

		var plugins = pluginLoader.Load<IConnectedSubsystemsPlugin>();
		var subsystems = new List<ISubsystem> { this };
		foreach (var plugin in plugins) {
			Log.Information("Loaded ConnectedSubsystemsPlugin plugin: {plugin} {version}.",
				plugin.CommandLineName,
				plugin.Version);
			subsystems.AddRange(plugin.GetSubsystems());
		}

		return subsystems;
	}

	public override void ConfigureServices(IServiceCollection services, IConfiguration configuration) {
		services.AddSingleton<IClient, InternalClient>();
		services.AddSingleton<IOperationsClient, InternalOperationsClient>();
	}
}
