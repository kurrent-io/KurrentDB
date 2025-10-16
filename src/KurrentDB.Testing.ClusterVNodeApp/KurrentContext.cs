// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Grpc.Net.Client;
using KurrentDB.Connectors.Management.Contracts.Commands;
using KurrentDB.Protocol.V2.Streams;
using RestSharp;
using RestSharp.Authenticators;
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
	public StreamsService.StreamsServiceClient StreamsV2Client { get; private set; } = null!;

	public Task InitializeAsync() {
		ConnectorsClient = new(GrpcChannelShim.GrpcChannel);
		StreamsV2Client = new(GrpcChannelShim.GrpcChannel);
		return Task.CompletedTask;
	}
}

public sealed class GrpcChannelShim : IAsyncInitializer, IAsyncDisposable {
	[ClassDataSource<NodeShim>(Shared = SharedType.PerTestSession)]
	public required NodeShim NodeShim { get; init; }

	public GrpcChannel GrpcChannel { get; private set; } = null!;

	public Task InitializeAsync() {
		GrpcChannel = GrpcChannel.ForAddress(NodeShim.Node.Uri);
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync() {
		GrpcChannel?.Dispose();
		return ValueTask.CompletedTask;
	}
}

public sealed class RestClientShim : IAsyncInitializer, IDisposable {
	[ClassDataSource<NodeShim>(Shared = SharedType.PerTestSession)]
	public required NodeShim KurrentDBNode { get; init; }

	public IRestClient Client { get; private set; } = null!;

	public Task InitializeAsync() {
		Client =
			new RestClient(new RestClientOptions() {
				Authenticator = new HttpBasicAuthenticator(
					username: TestContext.Configuration.Get("Node:Username") ?? "admin",
					password: TestContext.Configuration.Get("Node:Password") ?? "changeit"),
				BaseUrl = KurrentDBNode.Node.Uri,
				ThrowOnAnyError = true,
			}).AddDefaultHeaders(new() {
				{ "Content-Type", "application/json"},
			});

		return Task.CompletedTask;
	}

	public void Dispose() {
		Client?.Dispose();
	}
}
