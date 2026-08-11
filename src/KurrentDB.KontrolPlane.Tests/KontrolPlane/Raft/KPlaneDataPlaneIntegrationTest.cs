// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using DotNext;
using KurrentDB.Core.XUnit.Tests;

namespace KurrentDB.KontrolPlane.Raft;

// End-to-end tests that wire together a real 3-node RaftKontroller cluster (Kontrol Plane) and
// real DatabaseNodeHost instances (Data Plane), talking to each other over real gRPC/TCP connections.
//
// Every Kontrol Plane node exposes two endpoints on the same loopback address: the Raft wire
// protocol (raw TCP, used for consensus) and the gRPC KontrollerServer (used by the Data Plane).
// The gRPC port is derived from the Raft port with a fixed offset (see GrpcPortOffset); TestKontrollerServer
// translates Raft peer addresses to their gRPC counterparts via that same offset.
[Collection("RaftKontroller")]
public sealed partial class KPlaneDataPlaneIntegrationTest : DirectoryFixture<KPlaneDataPlaneIntegrationTest> {
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
				// check whether the database cluster is available
				var clusterInfo = await dataPlane[0].Host.GetDatabaseInfoAsync(TestToken);
				Assert.True(dbNodeAddresses
					.ToHashSet<EndPoint>()
					.SetEquals(clusterInfo.Nodes.Select(static m => m.Address)));

				// check whether the leader is appointed
				var leaderInfo = await dataPlane[0].Host.GetDatabaseLeadersAsync(TestToken).FirstOrDefaultAsync(TestToken);
				Assert.NotNull(leaderInfo);
				Assert.Contains(leaderInfo.Leader.Address, dbNodeAddresses);

				var leaderNode = dataPlane.First(node => node.Host.CurrentNode.Address.Equals(leaderInfo.Leader.Address));
				Assert.False(leaderNode.Host.LeadershipToken.IsCancellationRequested);
			} finally {
				await Disposable.DisposeAsync(dataPlane);
			}
		} finally {
			await Disposable.DisposeAsync(kplane);
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
				await WaitForRegisteredNodesAsync(dataPlane[0].Host, dbNodeAddresses.ToHashSet<EndPoint>(), TestToken);

				var leader = await dataPlane[0].Host.GetDatabaseLeadersAsync(TestToken).FirstOrDefaultAsync(TestToken);
				Assert.NotNull(leader);
			} finally {
				await Disposable.DisposeAsync(dataPlane);
			}
		} finally {
			await Disposable.DisposeAsync(kplane);
		}
	}

	[Fact(Timeout = 90_000)]
	public async Task DataPlaneReconnects_when_kplane_leader_changes() {
		var raftPorts = new[] { 23201, 23202, 23203 };
		var dbNodeAddresses = new[] { 23221, 23222, 23223 }.Select(CreateEndPoint).ToArray();
		var appointmentDuration = TimeSpan.FromSeconds(1);

		var kplane = await StartKPlaneClusterAsync(raftPorts, appointmentDuration, "kplane3");
		try {
			var databaseNodes = dbNodeAddresses.Select(CreateDatabaseNode).ToArray();
			var dataPlane = await StartDataPlaneClusterAsync(kplane, databaseNodes);
			try {
				foreach (var node in databaseNodes) {
					await AddDatabaseNodeAsync(kplane, node, TestToken);
				}

				// GetDatabaseLeadersAsync reflects the cluster-wide appointment, not "am I the leader" - any
				// connected Data Plane node observes the same stream, so there's no need to know in advance
				// which one KPlane elects.
				await using var leaders = dataPlane[0].Host.GetDatabaseLeadersAsync(TestToken).GetAsyncEnumerator();
				Assert.True(await leaders.MoveNextAsync());
				Assert.Contains(leaders.Current.Leader.Address, dbNodeAddresses);
				var initialEpoch = leaders.Current.Epoch;

				// stop the current KPlane (Raft) leader
				var raftLeaderAddress = await kplane[0].Kontroller.WaitForLeaderAsync(TestToken);
				var raftLeaderIndex = Array.FindIndex(kplane, n => Equals(n.RaftAddress, raftLeaderAddress));
				Assert.True(raftLeaderIndex >= 0);
				await kplane[raftLeaderIndex].DisposeAsync();

				// the new KPlane leader's appointment cache starts empty, so it re-appoints under a fresh
				// epoch as soon as it takes over - even if the winning candidate ends up being the same DP
				// node as before (tie-breaking isn't guaranteed stable across a quorum change).
				Assert.True(await leaders.MoveNextAsync());
				Assert.True(leaders.Current.Epoch > initialEpoch);
				Assert.Contains(leaders.Current.Leader.Address, dbNodeAddresses);
			} finally {
				await Disposable.DisposeAsync(dataPlane);
			}
		} finally {
			await Disposable.DisposeAsync(kplane);
		}
	}

	[Fact(Timeout = 60_000)]
	public async Task DiscoverMembershipChanges() {
		var raftPorts = new[] { 23001, 23002, 23003 };
		var dbNodeAddresses = new[] { 23021, 23022, 23023 }.Select(CreateEndPoint).ToArray();

		var kplane = await StartKPlaneClusterAsync(raftPorts, TimeSpan.FromSeconds(1), "kplane1");
		try {
			var databaseNodes = dbNodeAddresses.Select(CreateDatabaseNode).ToArray();
			foreach (var node in databaseNodes) {
				await AddDatabaseNodeAsync(kplane, node, TestToken);
			}

			var dataPlane = await StartDataPlaneClusterAsync(kplane, databaseNodes);
			var enumerator = dataPlane[0]
				.Host
				.GetDatabaseMembershipChangesAsync(TestToken)
				.GetAsyncEnumerator();
			try {
				// check whether the database cluster is available
				Assert.True(await enumerator.MoveNextAsync());
				var members = enumerator.Current;
				Assert.True(dbNodeAddresses
					.ToHashSet<EndPoint>()
					.SetEquals(members.Select(static m => m.Address)));

				// Add one more node
				var addedNode = CreateDatabaseNode(CreateEndPoint(23024));
				await AddDatabaseNodeAsync(kplane, addedNode, TestToken);

				Assert.True(await enumerator.MoveNextAsync());
				Assert.Contains(addedNode, enumerator.Current);
			} finally {
				await enumerator.DisposeAsync();
				await Disposable.DisposeAsync(dataPlane);
			}
		} finally {
			await Disposable.DisposeAsync(kplane);
		}
	}
}
