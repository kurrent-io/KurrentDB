// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Client.Streams;
using KurrentDB.Core;
using KurrentDB.Core.Bus;
using KurrentDB.Protocol.V2.Streams;
using Microsoft.Extensions.DependencyInjection;

namespace KurrentDB.Projections.V2.Tests.Fixtures;

/// <summary>
/// A test fixture that provides access to a KurrentDB node with projections enabled.
/// Delegates to KurrentContext for node lifecycle and client creation.
/// </summary>
public sealed class ProjectionsNodeFixture {
	[ClassDataSource<KurrentContext>(Shared = SharedType.PerTestSession)]
	public required KurrentContext KurrentContext { get; init; }

	public ClusterVNodeOptions ServerOptions => KurrentContext.Node.ClusterVNodeOptions;
	public IServiceProvider Services => KurrentContext.Node.Services;
	public IPublisher MainQueue => Services.GetRequiredService<IPublisher>();
	public StreamsService.StreamsServiceClient StreamsClient => KurrentContext.StreamsV2Client;
	public Streams.StreamsClient V1StreamsClient => KurrentContext.StreamsClient;
	public EventStore.Client.Projections.Projections.ProjectionsClient ProjectionsClient => KurrentContext.ProjectionsClient;
}
