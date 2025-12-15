// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Client.PersistentSubscriptions;
using KurrentDB.Connectors.Management.Contracts.Commands;
using KurrentDB.Protocol.V2.Streams;
using KurrentDB.Protocol.V2.Indexes;
using TUnit.Core.Interfaces;

namespace KurrentDB.Testing;

public sealed class KurrentContext : IAsyncInitializer {
	[ClassDataSource<NodeShim>(Shared = SharedType.PerTestSession)]
	public required NodeShim NodeShim { get; init; }

	[ClassDataSource<GrpcChannelShim>()]
	public required GrpcChannelShim GrpcChannelShim { get; init; }

	[ClassDataSource<RestClientShim>()]
	public required RestClientShim RestClientShim { get; init; }

	public INode Node => NodeShim.Node;
	public ConnectorsCommandService.ConnectorsCommandServiceClient ConnectorsClient { get; private set; } = null!;
	public IndexesService.IndexesServiceClient CustomIndexesClient { get; private set; } = null!;
	public PersistentSubscriptions.PersistentSubscriptionsClient PersistentSubscriptionsClient { get; private set; } = null!;
	public EventStore.Client.Streams.Streams.StreamsClient StreamsClient { get; private set; } = null!;
	public StreamsService.StreamsServiceClient StreamsV2Client { get; private set; } = null!;

	public Task InitializeAsync() {
		ConnectorsClient = new(GrpcChannelShim.GrpcChannel);
		CustomIndexesClient = new(GrpcChannelShim.GrpcChannel);
		PersistentSubscriptionsClient = new(GrpcChannelShim.GrpcChannel);
		StreamsClient = new(GrpcChannelShim.GrpcChannel);
		StreamsV2Client = new(GrpcChannelShim.GrpcChannel);
		return Task.CompletedTask;
	}
}
