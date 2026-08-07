// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using KurrentDB.Core.XUnit.Tests;
using KurrentDB.KontrolPlane.Transport.Grpc;
using KurrentDB.DataPlane;
using KurrentDB.DataPlane.Transport.Grpc;

namespace KurrentDB.KontrolPlane.Raft;

// End-to-end tests that wire together a real 3-node RaftKontroller cluster (Kontrol Plane) and
// real DatabaseNodeHost instances (Data Plane), talking to each other over real gRPC/TCP connections.
//
// Every Kontrol Plane node exposes two endpoints on the same loopback address: the Raft wire
// protocol (raw TCP, used for consensus) and the gRPC KontrollerServer (used by the Data Plane).
// The gRPC port is derived from the Raft port with a fixed offset (see GrpcPortOffset); TestKontrollerServer
// translates Raft peer addresses to their gRPC counterparts via that same offset.
[Collection("RaftKontroller")]
public sealed class KPlaneDataPlaneIntegrationTest : DirectoryFixture<KPlaneDataPlaneIntegrationTest> {
	private const int GrpcPortOffset = 1000;

	static KPlaneDataPlaneIntegrationTest()
		=> AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

	[Fact(Timeout = 60_000)]
	public async Task AppointsDatabaseLeader_when_nodes_are_pre_registered() {
		var raftPorts = new[] { 23001, 23002, 23003 };
		var dbNodeAddresses = new[] { 23021, 23022, 23023 }.Select(CreateEndPoint).ToArray();

		var kplane = await StartKPlaneClusterAsync(raftPorts, TimeSpan.FromSeconds(1), "kplane1");
		try {
			var databaseNodes = dbNodeAddresses.Select(CreateDatabaseNode).ToArray();
			foreach (var node in databaseNodes) {
				await AddDatabaseNodeAsync(kplane, node, TestToken);
			}

			var dataPlane = await StartDataPlaneClusterAsync(kplane, databaseNodes);
			try {
				var (leaderAddress, epoch) = await WaitForDatabaseLeaderAsync(kplane[0], Database.MainDatabaseId, TestToken);

				Assert.Contains(leaderAddress, dbNodeAddresses);

				var leader = await WaitForDataPlaneLeadershipAsync(dataPlane, leaderAddress, TestToken);
				Assert.False(leader.Host.LeadershipToken.IsCancellationRequested);
				Assert.True(epoch > 0UL);
			} finally {
				await StopDataPlaneClusterAsync(dataPlane);
			}
		} finally {
			await StopKPlaneClusterAsync(kplane);
		}
	}

	[Fact(Timeout = 60_000)]
	public async Task AppointsDatabaseLeader_when_nodes_self_register_via_announcement() {
		var raftPorts = new[] { 23101, 23102, 23103 };
		var dbNodeAddresses = new[] { 23121, 23122, 23123 }.Select(CreateEndPoint).ToArray();

		var kplane = await StartKPlaneClusterAsync(raftPorts, TimeSpan.FromSeconds(1), "kplane2");
		try {
			// the database has no pre-configured nodes; the Data Plane side registers itself via announcement
			var database = await kplane[0].Kontroller.GetDatabaseAsync(Database.MainDatabaseId, TestToken);
			Assert.NotNull(database);
			Assert.Empty(database.Nodes);

			var databaseNodes = dbNodeAddresses.Select(CreateDatabaseNode).ToArray();

			var dataPlane = await StartDataPlaneClusterAsync(kplane, databaseNodes);
			try {
				await WaitForRegisteredNodesAsync(kplane[0], Database.MainDatabaseId, dbNodeAddresses, TestToken);

				var (leaderAddress, _) = await WaitForDatabaseLeaderAsync(kplane[0], Database.MainDatabaseId, TestToken);

				Assert.Contains(leaderAddress, dbNodeAddresses);

				var leader = await WaitForDataPlaneLeadershipAsync(dataPlane, leaderAddress, TestToken);
				Assert.False(leader.Host.LeadershipToken.IsCancellationRequested);
			} finally {
				await StopDataPlaneClusterAsync(dataPlane);
			}
		} finally {
			await StopKPlaneClusterAsync(kplane);
		}
	}

	[Fact(Timeout = 90_000)]
	public async Task DataPlaneReconnects_when_kplane_leader_changes() {
		var raftPorts = new[] { 23201, 23202, 23203 };
		var dbNodeAddresses = new[] { 23221, 23222, 23223 }.Select(CreateEndPoint).ToArray();
		// wider than the other tests' 1s: after a KPlane failover, KurrentDB.DataPlane.DatabaseNodeHost restarts its
		// leadership session (see ChangeDatabaseLeaderAsync's (true,true) case), which briefly reports
		// LeadershipToken as canceled while it stops the old renewal loop and starts a new one. A short
		// AppointmentDuration leaves too little slack for that restart to settle - especially under CPU
		// contention from adjacent tests in the same collection - before this test's one-shot assertion samples it.
		var appointmentDuration = TimeSpan.FromSeconds(3);

		var kplane = await StartKPlaneClusterAsync(raftPorts, appointmentDuration, "kplane3");
		try {
			var databaseNodes = dbNodeAddresses.Select(CreateDatabaseNode).ToArray();
			foreach (var node in databaseNodes) {
				await AddDatabaseNodeAsync(kplane, node, TestToken);
			}

			var dataPlane = await StartDataPlaneClusterAsync(kplane, databaseNodes);
			try {
				var (leaderAddress, _) = await WaitForDatabaseLeaderAsync(kplane[0], Database.MainDatabaseId, TestToken);
				var databaseLeader = await WaitForDataPlaneLeadershipAsync(dataPlane, leaderAddress, TestToken);
				Assert.False(databaseLeader.Host.LeadershipToken.IsCancellationRequested);

				// find and stop the current KPlane (Raft) leader
				var raftLeaderAddress = await kplane[0].Kontroller.WaitForLeaderAsync(TestToken);
				var raftLeaderIndex = Array.FindIndex(kplane, n => Equals(n.RaftAddress, raftLeaderAddress));
				Assert.True(raftLeaderIndex >= 0);

				var stoppedNode = kplane[raftLeaderIndex];
				var survivors = kplane.Where((_, i) => i != raftLeaderIndex).ToArray();

				await stoppedNode.DisposeAsync();

				// a new KPlane leader must be elected among the survivors; WaitForLeaderAsync can return the
				// stale (now-stopped) leader immediately if the survivor hasn't detected the loss yet, so poll
				// until it actually changes.
				await WaitForDifferentRaftLeaderAsync(survivors[0], raftLeaderAddress, TestToken);

				// give the renewal loop a few appointment cycles to prove it keeps succeeding against the new KPlane leader
				await Task.Delay(appointmentDuration * 4, TestToken);

				// the same node must still be recognized as leader by the Data Plane side; note the LeadershipToken
				// *object* isn't expected to be the same instance as before - KPlane can legitimately re-appoint the
				// same node under a new epoch (e.g. right after its own failover), which restarts the leadership
				// session and issues a fresh token even though the leader address never changed.
				Assert.Equal(leaderAddress, databaseLeader.Host.CurrentNode.Address);
				Assert.False(databaseLeader.Host.LeadershipToken.IsCancellationRequested);
			} finally {
				await StopDataPlaneClusterAsync(dataPlane);
			}
		} finally {
			await StopKPlaneClusterAsync(kplane);
		}
	}

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

	private static async Task StopKPlaneClusterAsync(IReadOnlyList<KPlaneNode> nodes) {
		foreach (var node in nodes) {
			await node.DisposeAsync();
		}
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

	private static async Task StopDataPlaneClusterAsync(IReadOnlyList<DPNode> nodes) {
		foreach (var node in nodes) {
			await node.DisposeAsync();
		}
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

			await Task.Delay(50, token);
		}
	}

	private static async Task<(EndPoint Leader, ulong Epoch)> WaitForDatabaseLeaderAsync(KPlaneNode node, string databaseId, CancellationToken token) {
		for (;;) {
			var database = await node.Kontroller.GetDatabaseAsync(databaseId, token);
			if (database?.LeaderAddress is { } leader)
				return (leader, database.Epoch);

			await Task.Delay(50, token);
		}
	}

	private static async Task WaitForDifferentRaftLeaderAsync(KPlaneNode node, EndPoint previousLeader, CancellationToken token) {
		for (;;) {
			var leader = await node.Kontroller.WaitForLeaderAsync(token);
			if (!Equals(leader, previousLeader))
				return;

			await Task.Delay(50, token);
		}
	}

	private static async Task WaitForRegisteredNodesAsync(KPlaneNode node, string databaseId, IReadOnlyCollection<EndPoint> expectedAddresses, CancellationToken token) {
		for (;;) {
			var database = await node.Kontroller.GetDatabaseAsync(databaseId, token);
			if (database is not null && expectedAddresses.All(address => database.Nodes.Any(n => n.Address.Equals(address))))
				return;

			await Task.Delay(50, token);
		}
	}

	private static async Task<DPNode> WaitForDataPlaneLeadershipAsync(IReadOnlyList<DPNode> dataPlane, EndPoint expectedLeader, CancellationToken token) {
		for (;;) {
			var leader = dataPlane.FirstOrDefault(n =>
				n.Host.CurrentNode.Address.Equals(expectedLeader) && !n.Host.LeadershipToken.IsCancellationRequested);

			if (leader is not null)
				return leader;

			await Task.Delay(50, token);
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
