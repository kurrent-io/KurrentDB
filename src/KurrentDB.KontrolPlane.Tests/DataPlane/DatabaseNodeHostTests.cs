// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace KurrentDB.DataPlane;

using KontrolPlane;

public class DatabaseNodeHostTests {
	[Fact]
	public async Task GetDatabaseInfoAsync_returns_the_announced_cluster_snapshot() {
		var currentNode = CreateNode(1112);
		var initialCluster = CreateCluster([currentNode]);
		var kontrolPlane = new StubKontrolPlane(initialCluster);

		var host = CreateHost(currentNode, kontrolPlane);

		await host.StartAsync(TestToken);
		try {
			var cluster = await host.GetDatabaseInfoAsync(TestToken);

			Assert.Same(initialCluster, cluster);
		} finally {
			await host.StopAsync(TestToken);
		}
	}

	[Fact]
	public async Task CurrentNode_reflects_the_latest_information_reported_by_kontrol_plane() {
		var currentNode = CreateNode(1112);
		var initialCluster = CreateCluster([currentNode]);
		var kontrolPlane = new StubKontrolPlane(initialCluster);

		var host = CreateHost(currentNode, kontrolPlane);

		await host.StartAsync(TestToken);
		try {
			Assert.Equal(currentNode, host.CurrentNode);

			await using var changes = host.GetDatabaseMembershipChangesAsync(TestToken).GetAsyncEnumerator();
			Assert.True(await changes.MoveNextAsync()); // baseline

			var modifiedNode = currentNode with { Role = DatabaseNodeRole.ReadOnlyReplica };
			kontrolPlane.PublishClusterUpdate(initialCluster with { Nodes = [modifiedNode] });

			Assert.True(await changes.MoveNextAsync());
			Assert.Equal(DatabaseNodeRole.ReadOnlyReplica, host.CurrentNode.Role);
		} finally {
			await host.StopAsync(TestToken);
		}
	}

	[Fact]
	public async Task LeadershipToken_is_canceled_and_GetDatabaseLeadersAsync_yields_the_new_leader_when_the_leader_changes() {
		var currentNode = CreateNode(1112);
		var otherNode = CreateNode(1114);

		var initialCluster = CreateCluster([currentNode, otherNode]) with {
			LeaderAddress = currentNode.Address,
			Epoch = 1UL,
		};

		var kontrolPlane = new StubKontrolPlane(initialCluster);
		var host = CreateHost(currentNode, kontrolPlane);

		await host.StartAsync(CancellationToken.None);
		try {
			// the current node is appointed as a leader right from the start
			Assert.False(host.LeadershipToken.IsCancellationRequested);

			await using var leaders = host.GetDatabaseLeadersAsync(TestToken).GetAsyncEnumerator();

			Assert.True(await leaders.MoveNextAsync());
			Assert.Equal(currentNode, leaders.Current.Leader);
			Assert.Equal(1UL, leaders.Current.Epoch);

			var leadershipToken = host.LeadershipToken;

			// Kontrol Plane appoints another node as the leader
			kontrolPlane.PublishClusterUpdate(initialCluster with { LeaderAddress = otherNode.Address, Epoch = 2UL });

			Assert.True(await leaders.MoveNextAsync());
			Assert.Equal(otherNode, leaders.Current.Leader);
			Assert.Equal(2UL, leaders.Current.Epoch);

			Assert.True(leadershipToken.IsCancellationRequested);
			Assert.True(host.LeadershipToken.IsCancellationRequested);
		} finally {
			await host.StopAsync(CancellationToken.None);
		}
	}

	[Fact]
	public async Task GetDatabaseMembershipChangesAsync_yields_a_new_snapshot_whenever_kontrol_plane_adds_removes_or_modifies_a_node() {
		var currentNode = CreateNode(1112);
		var nodeB = CreateNode(1114);

		var initialCluster = CreateCluster([currentNode]);
		var kontrolPlane = new StubKontrolPlane(initialCluster);
		var host = CreateHost(currentNode, kontrolPlane);

		await host.StartAsync(CancellationToken.None);
		try {
			await using var changes = host.GetDatabaseMembershipChangesAsync(TestToken).GetAsyncEnumerator();

			Assert.True(await changes.MoveNextAsync());
			Assert.True(changes.Current.SetEquals([currentNode]));

			// Kontrol Plane adds a node
			kontrolPlane.PublishClusterUpdate(initialCluster with { Nodes = [currentNode, nodeB] });
			Assert.True(await changes.MoveNextAsync());
			Assert.True(changes.Current.SetEquals([currentNode, nodeB]));

			// Kontrol Plane modifies a node (same address, different role)
			var modifiedNodeB = nodeB with { Role = DatabaseNodeRole.ReadOnlyReplica };
			kontrolPlane.PublishClusterUpdate(initialCluster with { Nodes = [currentNode, modifiedNodeB] });
			Assert.True(await changes.MoveNextAsync());
			Assert.True(changes.Current.SetEquals([currentNode, modifiedNodeB]));

			// Kontrol Plane removes a node
			kontrolPlane.PublishClusterUpdate(initialCluster with { Nodes = [currentNode] });
			Assert.True(await changes.MoveNextAsync());
			Assert.True(changes.Current.SetEquals([currentNode]));
		} finally {
			await host.StopAsync(TestToken);
		}
	}

	[Fact]
	public async Task ResignLeader_forwards_the_request_to_kontrol_plane() {
		var currentNode = CreateNode(1112);
		var initialCluster = CreateCluster([currentNode]);
		var kontrolPlane = new StubKontrolPlane(initialCluster);
		var host = CreateHost(currentNode, kontrolPlane);

		await host.StartAsync(TestToken);
		try {
			await host.ResignLeader(TestToken);

			Assert.Equal(1, kontrolPlane.ResignLeaderCallCount);
			Assert.Equal(currentNode.DatabaseId, kontrolPlane.LastResignedDatabaseId);
		} finally {
			await host.StopAsync(TestToken);
		}
	}

	[Fact]
	public async Task ResignLeader_throws_ObjectDisposedException_after_the_host_is_stopped() {
		var currentNode = CreateNode(1112);
		var initialCluster = CreateCluster([currentNode]);
		var kontrolPlane = new StubKontrolPlane(initialCluster);
		var host = CreateHost(currentNode, kontrolPlane);

		await host.StartAsync(TestToken);
		await host.StopAsync(TestToken);

		await Assert.ThrowsAsync<ObjectDisposedException>(() => host.ResignLeader(TestToken));
	}

	private static CancellationToken TestToken => TestContext.Current.CancellationToken;

	private static DatabaseNode CreateNode(int port) => new() {
		DatabaseId = Database.MainDatabaseId,
		Address = new IPEndPoint(IPAddress.Loopback, port),
		ReplicationProtocolAddress = new IPEndPoint(IPAddress.Loopback, port + 1),
	};

	private static DatabaseCluster CreateCluster(IReadOnlyList<DatabaseNode> nodes) => new() {
		Id = Database.MainDatabaseId,
		Nodes = nodes,
		// long enough to prevent the leadership renewal timer from firing during a test
		LeaderAppointmentDuration = TimeSpan.FromMinutes(10),
	};

	private static TestDatabaseNodeHost CreateHost(DatabaseNode currentNode, IKontrolPlane kontrolPlane) =>
		new(new DatabaseNodeHost.Options { CurrentNode = currentNode }) {
			KontrolPlane = kontrolPlane,
		};

	private sealed class TestDatabaseNodeHost(DatabaseNodeHost.Options options) : DatabaseNodeHost(options) {
		protected override ValueTask<ReplicaState> GetReplicaStateAsync(CancellationToken token)
			=> ValueTask.FromResult(new ReplicaState());
	}

	// Mimics the behavior of RaftKontroller as observed from the Data Plane side: AnnounceNodeAsync
	// immediately returns the first known version of the cluster and then keeps yielding every
	// subsequent version without ever completing the enumeration.
	private sealed class StubKontrolPlane(DatabaseCluster initialCluster) : IKontrolPlane {
		private readonly Channel<DatabaseCluster> _updates = Channel.CreateUnbounded<DatabaseCluster>();

		public int ResignLeaderCallCount;
		public string? LastResignedDatabaseId;

		public void PublishClusterUpdate(DatabaseCluster cluster) => _updates.Writer.TryWrite(cluster);

		public async IAsyncEnumerable<DatabaseCluster> AnnounceNodeAsync(DatabaseNode node,
			[EnumeratorCancellation] CancellationToken token = default) {
			yield return initialCluster;

			await foreach (var cluster in _updates.Reader.ReadAllAsync(token)) {
				yield return cluster;
			}
		}

		public Task<bool> RenewLeaderAppointmentAsync(string databaseId, EndPoint nodeAddress, ulong nodeEpoch,
			CancellationToken token = default)
			=> Task.FromResult(true);

		public Task ResignLeaderAsync(string databaseId, CancellationToken token = default) {
			token.ThrowIfCancellationRequested();
			Interlocked.Increment(ref ResignLeaderCallCount);
			LastResignedDatabaseId = databaseId;
			return Task.CompletedTask;
		}
	}
}
