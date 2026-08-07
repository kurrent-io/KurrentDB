// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KurrentDB.KontrolPlane.Transport.Grpc;

public class GrpcKontrolPlaneClientTests {
	[Fact]
	public async Task RenewLeaderAppointmentAsync_switches_to_the_reported_leader() {
		await using var nodeA = await KontrollerNode.StartAsync(new IPEndPoint(IPAddress.Loopback, 2113));
		await using var nodeB = await KontrollerNode.StartAsync(new IPEndPoint(IPAddress.Loopback, 2114));

		// nodeA is not the leader anymore, it redirects the caller to nodeB
		nodeA.Service.RenewLeaderAppointmentResponse = new() { KontrollerLeader = nodeB.Address.ToByteString() };

		// nodeB is the leader and accepts the request
		nodeB.Service.RenewLeaderAppointmentResponse = new() { Success = true };

		using var client = CreateClient(seed: nodeA.Address, nodeA, nodeB);

		var success = await client
			.RenewLeaderAppointmentAsync(Database.MainDatabaseId, new IPEndPoint(IPAddress.Loopback, 1112), nodeEpoch: 1UL, TestToken);

		Assert.True(success);
		Assert.Equal(1, nodeA.Service.RenewLeaderAppointmentCallCount);
		Assert.Equal(1, nodeB.Service.RenewLeaderAppointmentCallCount);
	}

	[Fact]
	public async Task AnnounceNodeAsync_switches_to_the_reported_leader() {
		await using var nodeA = await KontrollerNode.StartAsync(new IPEndPoint(IPAddress.Loopback, 2113));
		await using var nodeB = await KontrollerNode.StartAsync(new IPEndPoint(IPAddress.Loopback, 2114));

		var cluster = new KontrolPlane.DatabaseCluster {
			Id = Database.MainDatabaseId,
			LeaderAddress = new IPEndPoint(IPAddress.Loopback, 3113),
			LeaderAppointmentDuration = TimeSpan.FromSeconds(30),
		};

		// nodeA is not the leader anymore, it redirects the caller to nodeB
		nodeA.Service.AnnouncementResponse = new() { KontrollerLeader = nodeB.Address.ToByteString() };

		// nodeB is the leader and serves the cluster state
		nodeB.Service.AnnouncementResponse = new() { Cluster = new(cluster) };

		using var client = CreateClient(seed: nodeA.Address, nodeA, nodeB);

		var node = new KontrolPlane.DatabaseNode {
			DatabaseId = Database.MainDatabaseId,
			Address = new IPEndPoint(IPAddress.Loopback, 1112),
			ReplicationProtocolAddress = new IPEndPoint(IPAddress.Loopback, 1113),
		};

		await using var enumerator = client.AnnounceNodeAsync(node, TestToken).GetAsyncEnumerator();

		Assert.True(await enumerator.MoveNextAsync());
		Assert.Equal(Database.MainDatabaseId, enumerator.Current.Id);

		Assert.Equal(1, nodeA.Service.AnnounceDatabaseNodeCallCount);
		Assert.Equal(1, nodeB.Service.AnnounceDatabaseNodeCallCount);
	}

	private static CancellationToken TestToken => TestContext.Current.CancellationToken;

	private static GrpcKontrolPlaneClient CreateClient(EndPoint seed, params IEnumerable<KontrollerNode> nodes) =>
		new TestKontrolPlaneClient(nodes.ToDictionary(static n => n.Address, static n => n.Handler)) {
			KontrolPlaneNodes = new HashSet<EndPoint> { seed },
		};

	// Every RPC just returns whatever response the test configured beforehand, so each test
	// fully controls what a "node" answers with (e.g. redirect to another leader vs. accept the request).
	private sealed class FakeKontrollerService : Kontroller.KontrollerBase {
		public RenewLeaderAppointmentResponse RenewLeaderAppointmentResponse = new();
		public AnnouncementResponse AnnouncementResponse = new();

		public int RenewLeaderAppointmentCallCount;
		public int AnnounceDatabaseNodeCallCount;

		public override Task<RenewLeaderAppointmentResponse> RenewLeaderAppointment(RenewLeaderAppointmentRequest request, ServerCallContext context) {
			Interlocked.Increment(ref RenewLeaderAppointmentCallCount);
			return Task.FromResult(RenewLeaderAppointmentResponse);
		}

		public override async Task AnnounceDatabaseNode(AnnouncementRequest request, IServerStreamWriter<AnnouncementResponse> responseStream, ServerCallContext context) {
			Interlocked.Increment(ref AnnounceDatabaseNodeCallCount);
			await responseStream.WriteAsync(AnnouncementResponse);
		}
	}

	private sealed class TestKontrolPlaneClient(IReadOnlyDictionary<EndPoint, HttpMessageHandler> handlers) : GrpcKontrolPlaneClient {
		protected override IDisposable CreateChannel(EndPoint address, out CallInvoker invoker) {
			var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions {
				HttpHandler = handlers[address],
			});

			invoker = channel.CreateCallInvoker();
			return channel;
		}
	}

	// A single fake Kontroller node: an in-memory ASP.NET Core host plus the gRPC handler used to reach it.
	private sealed class KontrollerNode(IHost host, EndPoint address, FakeKontrollerService service) : IAsyncDisposable {
		public EndPoint Address => address;
		public FakeKontrollerService Service => service;
		public HttpMessageHandler Handler { get; } = host.GetTestServer().CreateHandler();

		public static async Task<KontrollerNode> StartAsync(EndPoint address) {
			var service = new FakeKontrollerService();

			var host = await new HostBuilder()
				.ConfigureWebHost(webHost => webHost
					.UseTestServer()
					.ConfigureServices(services => {
						services.AddSingleton(service);
						services.AddGrpc();
					})
					.Configure(app => app
						.UseRouting()
						.UseEndpoints(endpoints => endpoints.MapGrpcService<FakeKontrollerService>())))
				.StartAsync(TestToken);

			return new(host, address, service);
		}

		public async ValueTask DisposeAsync() {
			await host.StopAsync(TestToken);
			host.Dispose();
		}
	}
}
