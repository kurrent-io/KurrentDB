// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KurrentDB.DataPlane.Transport.Grpc;

public class GrpcDataPlaneClientTests {
	[Fact]
	public async Task GetReplicaStateAsync_returns_mapped_replica_state() {
		await using var node = await DataPlaneNodeHost.StartAsync(new IPEndPoint(IPAddress.Loopback, 1112));

		var instanceId = Guid.NewGuid();
		node.Service.Response = new() {
			Epoch = 5UL,
			WriterCheckpoint = 100L,
			ChaserCheckpoint = 99L,
			Priority = 2,
			InstanceId = ByteString.CopyFrom(instanceId.ToByteArray()),
		};

		await using var client = CreateClient(node);

		var state = await client.FenceAsync(node.Address, 6UL, TestToken);

		Assert.Equal(5UL, state.Epoch);
		Assert.Equal(100L, state.WriterCheckpoint);
		Assert.Equal(99L, state.ChaserCheckpoint);
		Assert.Equal(2, state.Priority);
		Assert.Equal(instanceId, state.InstanceId);
	}

	[Fact]
	public async Task GetReplicaStateAsync_reuses_the_cached_channel_for_the_same_address() {
		await using var node = await DataPlaneNodeHost.StartAsync(new IPEndPoint(IPAddress.Loopback, 1112));

		await using var client = CreateClient(node);

		await client.FenceAsync(node.Address, 1UL, TestToken);
		await client.FenceAsync(node.Address, 2UL, TestToken);

		Assert.Equal(1, client.ChannelsCreatedCount);
		Assert.Equal(2, node.Service.CallCount);
	}

	[Fact]
	public async Task ReclaimConnectionsAsync_drops_channels_that_are_no_longer_active() {
		await using var nodeA = await DataPlaneNodeHost.StartAsync(new IPEndPoint(IPAddress.Loopback, 1112));
		await using var nodeB = await DataPlaneNodeHost.StartAsync(new IPEndPoint(IPAddress.Loopback, 1113));

		await using var client = CreateClient(nodeA, nodeB);

		await client.FenceAsync(nodeA.Address, 1UL, TestToken);
		await client.FenceAsync(nodeB.Address, 2UL, TestToken);
		Assert.Equal(2, client.ChannelsCreatedCount);

		// keep nodeA alive, drop nodeB
		await client.ReclaimConnectionsAsync(new HashSet<EndPoint> { nodeA.Address }, TestToken);

		Assert.Equal(1, client.ChannelsDisposedCount);

		// nodeA's cached channel is still there, so no new channel is created
		await client.FenceAsync(nodeA.Address, 3UL, TestToken);
		Assert.Equal(2, client.ChannelsCreatedCount);

		// nodeB's channel was reclaimed, so a fresh one is created on the next call
		await client.FenceAsync(nodeB.Address, 0UL, TestToken);
		Assert.Equal(3, client.ChannelsCreatedCount);
	}

	[Fact]
	public async Task DisposeAsync_disposes_all_cached_channels() {
		await using var nodeA = await DataPlaneNodeHost.StartAsync(new IPEndPoint(IPAddress.Loopback, 1112));
		await using var nodeB = await DataPlaneNodeHost.StartAsync(new IPEndPoint(IPAddress.Loopback, 1113));

		var client = CreateClient(nodeA, nodeB);

		await client.FenceAsync(nodeA.Address, 1UL, TestToken);
		await client.FenceAsync(nodeB.Address, 2UL, TestToken);

		await client.DisposeAsync();

		Assert.Equal(2, client.ChannelsDisposedCount);
	}

	private static CancellationToken TestToken => TestContext.Current.CancellationToken;

	private static TestDataPlaneClient CreateClient(params IEnumerable<DataPlaneNodeHost> nodes) =>
		new(nodes.ToDictionary(static n => n.Address, static n => n.Handler));

	// Every RPC just returns whatever response the test configured beforehand.
	private sealed class FakeDataPlaneNodeService : DataPlaneNode.DataPlaneNodeBase {
		public FenceResponse Response = new();

		public int CallCount;

		public override Task<FenceResponse> Fence(FenceRequest request, ServerCallContext context) {
			Interlocked.Increment(ref CallCount);
			return Task.FromResult(Response);
		}
	}

	private sealed class TestDataPlaneClient(IReadOnlyDictionary<EndPoint, HttpMessageHandler> handlers) : GrpcDataPlaneClient {
		public int ChannelsCreatedCount;
		public int ChannelsDisposedCount;

		protected override IDisposable CreateChannel(EndPoint address, out CallInvoker invoker) {
			Interlocked.Increment(ref ChannelsCreatedCount);

			var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions {
				HttpHandler = handlers[address],
			});

			invoker = channel.CreateCallInvoker();
			return new TrackedDisposable(channel, this);
		}

		private sealed class TrackedDisposable(IDisposable channel, TestDataPlaneClient owner) : IDisposable {
			public void Dispose() {
				channel.Dispose();
				Interlocked.Increment(ref owner.ChannelsDisposedCount);
			}
		}
	}

	// A single fake DataPlane node: an in-memory ASP.NET Core host plus the gRPC handler used to reach it.
	private sealed class DataPlaneNodeHost(IHost host, EndPoint address, FakeDataPlaneNodeService service) : IAsyncDisposable {
		public EndPoint Address => address;
		public FakeDataPlaneNodeService Service => service;
		public HttpMessageHandler Handler { get; } = host.GetTestServer().CreateHandler();

		public static async Task<DataPlaneNodeHost> StartAsync(EndPoint address) {
			var service = new FakeDataPlaneNodeService();

			var host = await new HostBuilder()
				.ConfigureWebHost(webHost => webHost
					.UseTestServer()
					.ConfigureServices(services => {
						services.AddSingleton(service);
						services.AddGrpc();
					})
					.Configure(app => app
						.UseRouting()
						.UseEndpoints(endpoints => endpoints.MapGrpcService<FakeDataPlaneNodeService>())))
				.StartAsync(TestToken);

			return new(host, address, service);
		}

		public async ValueTask DisposeAsync() {
			await host.StopAsync(TestToken);
			host.Dispose();
		}
	}
}
