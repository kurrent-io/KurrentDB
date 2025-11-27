// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using Grpc.Core;
using Grpc.Net.Client;
using TUnit.Core.Interfaces;

namespace KurrentDB.Testing;

public sealed class GrpcChannelShim : IAsyncInitializer, IAsyncDisposable {
	[ClassDataSource<NodeShim>(Shared = SharedType.PerTestSession)]
	public required NodeShim NodeShim { get; init; }

	public GrpcChannel GrpcChannel { get; private set; } = null!;

	public Task InitializeAsync() {
		if (NodeShim.NodeShimOptions.Insecure) {
			GrpcChannel = GrpcChannel.ForAddress(NodeShim.Node.Uri);
		} else {
			var username = NodeShim.NodeShimOptions.Username;
			var password = NodeShim.NodeShimOptions.Password;

			var credentials = CallCredentials.FromInterceptor(async (context, metadata) => {
				metadata.Add(
					"Authorization",
					$"Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"))}");
			});

			var channelOptions = new GrpcChannelOptions() {
				Credentials = ChannelCredentials.Create(new SslCredentials(), credentials),
				HttpHandler = new HttpClientHandler {
					ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
				},
			};

			GrpcChannel = GrpcChannel.ForAddress(NodeShim.Node.Uri, channelOptions);
		}

		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync() {
		GrpcChannel?.Dispose();
		return ValueTask.CompletedTask;
	}
}
