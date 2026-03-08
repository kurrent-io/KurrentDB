// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Net.Client;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Protocol.V2.Streams;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TUnit.Core.Interfaces;

namespace KurrentDB.Projections.V2.Tests.Fixtures;

/// <summary>
/// A test fixture that spins up a KurrentDB node with projections enabled.
/// Shared across all tests in the session to avoid expensive restarts.
/// </summary>
public sealed class ProjectionsNodeFixture : IAsyncInitializer, IAsyncDisposable {
	static readonly Dictionary<string, object?> Overrides = new() {
		{ "KurrentDB:Projection:RunProjections", "All" },
		{ "KurrentDB:Projection:StartStandardProjections", true },
		{ "KurrentDB:Projection:ProjectionThreads", 3 },
	};

	ClusterVNodeApp _node = null!;
	GrpcChannel _channel = null!;

	public ClusterVNodeOptions ServerOptions => _node.ServerOptions;
	public IServiceProvider Services => _node.Services;
	public IPublisher MainQueue => Services.GetRequiredService<IPublisher>();
	public StreamsService.StreamsServiceClient StreamsClient { get; private set; } = null!;
	public EventStore.Client.Projections.Projections.ProjectionsClient ProjectionsClient { get; private set; } = null!;

	public async Task InitializeAsync() {
		// Force-load all assemblies from the output directory so InMemoryBus discovers
		// all message types (including projection messages) in its static constructor.
		foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "KurrentDB.*.dll")) {
			try { System.Reflection.Assembly.LoadFrom(dll); } catch { /* ignore load failures */ }
		}

		_node = new ClusterVNodeApp(
			configureServices: static (_, services) => {
				services
					.AddSingleton<ILoggerFactory, ToolkitTestLoggerFactory>()
					.AddTestTimeProvider();
			},
			overrides: Overrides
		);

		await _node.Start(readinessTimeout: TimeSpan.FromSeconds(30));

		var uri = Services.GetServerLocalAddress();
		_channel = GrpcChannel.ForAddress(uri);
		StreamsClient = new StreamsService.StreamsServiceClient(_channel);
		ProjectionsClient = new EventStore.Client.Projections.Projections.ProjectionsClient(_channel);
	}

	public async ValueTask DisposeAsync() {
		_channel?.Dispose();
		await _node.DisposeAsync();
	}
}
