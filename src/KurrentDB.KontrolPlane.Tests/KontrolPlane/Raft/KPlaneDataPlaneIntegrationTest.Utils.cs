// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using KurrentDB.KontrolPlane.Transport.Grpc;
using KurrentDB.DataPlane;
using KurrentDB.DataPlane.Transport.Grpc;

namespace KurrentDB.KontrolPlane.Raft;

partial class KPlaneDataPlaneIntegrationTest {
	private const int GrpcPortOffset = 1000;

	static KPlaneDataPlaneIntegrationTest()
		=> AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

	private async Task<KPlaneNode[]> StartKPlaneClusterAsync(IReadOnlyList<int> raftPorts, TimeSpan appointmentDuration, string stateRootName) {
		var raftAddresses = raftPorts.Select(CreateEndPoint).ToHashSet<EndPoint>();

		var nodes = new KPlaneNode[raftPorts.Count];
		for (var i = 0; i < raftPorts.Count; i++) {
			nodes[i] = await KPlaneNode.StartAsync(
				Path.Combine(Directory, stateRootName, i.ToString()),
				CreateEndPoint(raftPorts[i]),
				raftAddresses,
				appointmentDuration,
				CreateRealDataPlaneClient,
				TestToken);
		}

		return nodes;
	}

	// Every node reports a distinct WriterCheckpoint so leader-appointment tie-breaking is deterministic:
	// the same candidate (index 0) wins every re-appointment instead of flapping between equally-ranked nodes.
	private static async Task<DPNode[]> StartDataPlaneClusterAsync(IReadOnlyList<KPlaneNode> kplane, IReadOnlyList<DatabaseNode> databaseNodes) {
		var kontrolPlaneNodes = kplane.Select(static n => n.GrpcAddress).ToHashSet<EndPoint>();

		var nodes = new DPNode[databaseNodes.Count];
		for (var i = 0; i < databaseNodes.Count; i++) {
			var replicaState = new ReplicaState(Epoch: 0UL, WriterCheckpoint: databaseNodes.Count - i, ChaserCheckpoint: 0L, Priority: 0);
			nodes[i] = await DPNode.StartAsync(databaseNodes[i], kontrolPlaneNodes, replicaState, TestToken);
		}

		return nodes;
	}

	private static async Task AddDatabaseNodeAsync(IReadOnlyList<KPlaneNode> kplane, DatabaseNode node, CancellationToken token) {
		for (;;) {
			foreach (var kplaneNode in kplane) {
				try {
					await kplaneNode.Kontroller.AddOrUpdateDatabaseNodeAsync(node, token);
					return;
				} catch (LeadershipRequiredException) {
					// try the next candidate; none of the KPlane nodes may be the leader yet
				}
			}

			await kplane[0].Kontroller.WaitForLeaderAsync(token);
		}
	}

	private static async Task WaitForRegisteredNodesAsync(IDatabaseNode node, IReadOnlySet<EndPoint> expectedAddresses, CancellationToken token) {
		await foreach (var members in node.GetDatabaseMembershipChangesAsync(token)) {
			if (expectedAddresses.SetEquals(members.Select(static m => m.Address)))
				return;
		}
	}

	private static DatabaseNode CreateDatabaseNode(EndPoint address) => new() {
		DatabaseId = Database.MainDatabaseId,
		Address = address,
		ReplicationProtocolAddress = address,
	};

	private static IPEndPoint CreateEndPoint(int port) => new(IPAddress.Loopback, port);

	private static IPEndPoint ToGrpcEndPoint(EndPoint raftAddress) {
		var ip = (IPEndPoint)raftAddress;
		return new(ip.Address, ip.Port + GrpcPortOffset);
	}

	private static IDataPlane CreateRealDataPlaneClient() => new RealGrpcDataPlaneClient();

	private static IDisposable CreateHttp2Channel(EndPoint address, out CallInvoker invoker) {
		var channel = GrpcChannel.ForAddress($"http://{address}", new GrpcChannelOptions {
			HttpHandler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true },
		});

		invoker = channel.CreateCallInvoker();
		return channel;
	}

	private static CancellationToken TestToken => TestContext.Current.CancellationToken;

	private sealed class RealGrpcKontrolPlaneClient : GrpcKontrolPlaneClient {
		protected override IDisposable CreateChannel(EndPoint address, out CallInvoker invoker)
			=> CreateHttp2Channel(address, out invoker);
	}

	private sealed class RealGrpcDataPlaneClient : GrpcDataPlaneClient {
		protected override IDisposable CreateChannel(EndPoint address, out CallInvoker invoker)
			=> CreateHttp2Channel(address, out invoker);
	}

	private sealed class TestKontrollerServer(IKontroller kontroller) : KontrollerServer(kontroller) {
		protected override EndPoint GetApiEndPoint(EndPoint nodeEndPoint) => ToGrpcEndPoint(nodeEndPoint);
	}

	private sealed class TestDatabaseNodeHost(DatabaseNodeHost.Options options, ReplicaState replicaState) : DatabaseNodeHost(options) {
		protected override ValueTask<ReplicaState> GetReplicaStateAsync(CancellationToken token)
			=> ValueTask.FromResult(replicaState);
	}

	// A single Kontrol Plane node: a real RaftKontroller (Raft consensus over real TCP) plus a real
	// Kestrel-hosted gRPC KontrollerServer, so the Data Plane side talks to it exactly as it would in production.
	private sealed class KPlaneNode : IAsyncDisposable {
		public required RaftKontroller Kontroller { get; init; }
		public required WebApplication GrpcHost { get; init; }
		public required IPEndPoint RaftAddress { get; init; }
		public required IPEndPoint GrpcAddress { get; init; }

		public static async Task<KPlaneNode> StartAsync(
			string stateRoot,
			IPEndPoint raftAddress,
			IReadOnlySet<EndPoint> raftSeed,
			TimeSpan appointmentDuration,
			Func<IDataPlane> dataPlaneClientFactory,
			CancellationToken token) {
			System.IO.Directory.CreateDirectory(stateRoot);

			var kontroller = new RaftKontroller(new RaftKontroller.Options {
				ListenAddress = raftAddress,
				AppointmentDuration = appointmentDuration,
				ConnectionPoolCapacity = 10,
				PersistentStateRoot = stateRoot,
				Nodes = raftSeed,
			}) {
				DataPlaneClientFactory = dataPlaneClientFactory,
			};

			await kontroller.StartAsync(token);

			var grpcAddress = ToGrpcEndPoint(raftAddress);

			var builder = WebApplication.CreateBuilder();
			builder.WebHost.ConfigureKestrel(o => o.Listen(grpcAddress, lo => lo.Protocols = HttpProtocols.Http2));
			builder.Services.AddSingleton<IKontroller>(kontroller);
			builder.Services.AddGrpc();

			var app = builder.Build();
			app.MapGrpcService<TestKontrollerServer>();
			await app.StartAsync(token);

			return new() {
				Kontroller = kontroller,
				GrpcHost = app,
				RaftAddress = raftAddress,
				GrpcAddress = grpcAddress,
			};
		}

		private bool _disposed;

		public async ValueTask DisposeAsync() {
			if (_disposed)
				return;
			_disposed = true;

			await GrpcHost.StopAsync();
			await GrpcHost.DisposeAsync();
			await Kontroller.StopAsync(CancellationToken.None);
			await Kontroller.DisposeAsync();
		}
	}

	// A single Data Plane node: a real DatabaseNodeHost plus a real Kestrel-hosted gRPC DataPlaneServer,
	// exposed on the address the Kontrol Plane will use to query its replica state.
	private sealed class DPNode(TestDatabaseNodeHost host, WebApplication webApp) : IAsyncDisposable {
		public IDatabaseNode Host => host;

		public static async Task<DPNode> StartAsync(
			DatabaseNode currentNode,
			IReadOnlySet<EndPoint> kontrolPlaneNodes,
			ReplicaState replicaState,
			CancellationToken token) {
			var host = new TestDatabaseNodeHost(new DatabaseNodeHost.Options { CurrentNode = currentNode }, replicaState) {
				KontrolPlane = new RealGrpcKontrolPlaneClient { KontrolPlaneNodes = kontrolPlaneNodes },
			};

			var builder = WebApplication.CreateBuilder();
			builder.WebHost.ConfigureKestrel(o => o.Listen((IPEndPoint)currentNode.Address, lo => lo.Protocols = HttpProtocols.Http2));
			builder.Services.AddSingleton<DatabaseNodeHost>(host);
			builder.Services.AddGrpc();

			var app = builder.Build();
			app.MapGrpcService<DataPlaneServer>();
			await app.StartAsync(token);

			await host.StartAsync(token);

			return new(host, app);
		}

		public async ValueTask DisposeAsync() {
			await host.StopAsync(CancellationToken.None);
			await webApp.StopAsync();
			await webApp.DisposeAsync();
		}
	}
}
