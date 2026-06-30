// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Immutable;
using System.Net;
using KurrentDB.Core.XUnit.Tests;

namespace KurrentDB.KontrolPlane;

public sealed class LeaderAppointmentTests : DirectoryFixture<LeaderAppointmentTests> {
	[Fact]
	public async Task AppointLeader() {
		// initialize members
		var replicaSet = new TestReplicaSet();
		replicaSet.UpdateMember(TestReplicaSet.Host1, new(UncommittedOffset: 100, Epoch: 1L));
		replicaSet.UpdateMember(TestReplicaSet.Host2, new(UncommittedOffset: 200, Epoch: 0L));
		replicaSet.UpdateMember(TestReplicaSet.Host3, new(UncommittedOffset: 150, Epoch: 1L)); // leader

		// initialize Kontroller
		await using var kontroller = new RaftKontroller(new RaftKontroller.Options {
			ListenAddress = new(IPAddress.Loopback, 3269),
			AppointmentExpiration = TimeSpan.FromSeconds(1),
			ConnectionPoolCapacity = 10,
			WalOptions = new() {
				Location = Directory,
			},
			SingleNodeDeployment = true,
		}) {
			ReplicaSet = replicaSet,
		};

		await kontroller.StartAsync(TestToken);
		await AddNodesAsync(kontroller);

		var leader = await WaitForLeaderAsync(kontroller);
		Assert.Equal(TestReplicaSet.Host3, leader.Address);
		Assert.Equal(1UL, leader.Epoch);

		await kontroller.StopAsync(TestToken);
	}

	[Fact]
	public async Task RenewLeaderAppointment() {
		// initialize members
		var replicaSet = new TestReplicaSet();
		replicaSet.UpdateMember(TestReplicaSet.Host1, new(UncommittedOffset: 100, Epoch: 1L));
		replicaSet.UpdateMember(TestReplicaSet.Host2, new(UncommittedOffset: 200, Epoch: 0L));
		replicaSet.UpdateMember(TestReplicaSet.Host3, new(UncommittedOffset: 150, Epoch: 1L)); // leader

		// initialize Kontroller
		await using var kontroller = new RaftKontroller(new RaftKontroller.Options {
			ListenAddress = new(IPAddress.Loopback, 3269),
			AppointmentExpiration = TimeSpan.FromSeconds(1),
			ConnectionPoolCapacity = 10,
			WalOptions = new() {
				Location = Directory,
			},
			SingleNodeDeployment = true,
		}) {
			ReplicaSet = replicaSet,
		};

		await kontroller.StartAsync(TestToken);
		await AddNodesAsync(kontroller);

		var leader = await WaitForLeaderAsync(kontroller);
		Assert.Equal(TestReplicaSet.Host3, leader.Address);

		// Start renewal process in the background
		var process = new RenewalProcess(kontroller, TestReplicaSet.Host3, leader.Epoch);
		var renewalTask = process.RunAsync();

		// Change state of another member so it should be chosen as a leader, but due to renewal it cannot be appointed
		replicaSet.UpdateMember(TestReplicaSet.Host1, new(UncommittedOffset: 300, Epoch: 3L));

		// Now stop renewal process and wait for new leader appointment
		process.RequestStop();
		await renewalTask.WaitAsync(TestToken);

		do {
			leader = await WaitForLeaderAsync(kontroller);
		} while (!TestReplicaSet.Host1.Equals(leader.Address));

		await kontroller.StopAsync(TestToken);
	}

	private sealed class RenewalProcess(IKontroller kontroller, EndPoint address, ulong epoch) {
		private volatile bool _stopped;

		public void RequestStop() => _stopped = true;

		public async Task RunAsync() {
			while (!_stopped) {
				// Renew every 300 ms
				await Task.Delay(300);

				await kontroller.RenewLeaderAppointmentAsync(Database.MainDatabaseId, address, epoch, TestToken);
			}
		}
	}

	private static async Task AddNodesAsync(IKontroller kontroller) {
		var node = new DatabaseNode { DatabaseId = Database.MainDatabaseId, Address = TestReplicaSet.Host1 };
		await kontroller.AddOrUpdateDatabaseNodeAsync(node, TestToken);

		node = new DatabaseNode { DatabaseId = Database.MainDatabaseId, Address = TestReplicaSet.Host2 };
		await kontroller.AddOrUpdateDatabaseNodeAsync(node, TestToken);

		node = new DatabaseNode { DatabaseId = Database.MainDatabaseId, Address = TestReplicaSet.Host3 };
		await kontroller.AddOrUpdateDatabaseNodeAsync(node, TestToken);

		node = new DatabaseNode { DatabaseId = Database.MainDatabaseId, Address = TestReplicaSet.Host4 };
		await kontroller.AddOrUpdateDatabaseNodeAsync(node, TestToken);
	}

	private static async Task<(EndPoint? Address, ulong Epoch)> WaitForLeaderAsync(IKontroller kontroller) {
		await foreach (var database in kontroller.ListenDatabaseAsync(Database.MainDatabaseId, TestToken)) {
			if (database.LeaderAddress is { } address)
				return (address, database.Epoch);
		}

		return (null, 0L);
	}

	private static CancellationToken TestToken => TestContext.Current.CancellationToken;

	private sealed class TestReplicaSet : IDatabaseReplicaSet {
		public static readonly IPEndPoint Host1 = new(IPAddress.Loopback, 3269);
		public static readonly IPEndPoint Host2 = new(IPAddress.Loopback, 3270);
		public static readonly IPEndPoint Host3 = new(IPAddress.Loopback, 3271);
		public static readonly IPEndPoint Host4 = new(IPAddress.Loopback, 3271);

		private ImmutableDictionary<EndPoint, ReplicaState> _members = ImmutableDictionary<EndPoint, ReplicaState>.Empty;

		public void UpdateMember(IPEndPoint endPoint, ReplicaState state) {
			for (ImmutableDictionary<EndPoint, ReplicaState> current = _members, tmp;; current = tmp) {
				var newDictionary = current.SetItem(endPoint, state);
				tmp = Interlocked.CompareExchange(ref _members, newDictionary, current);

				if (ReferenceEquals(current, tmp))
					break;
			}
		}

		ValueTask<ReplicaState> IDatabaseReplicaSet.GetReplicaStateAsync(EndPoint address, CancellationToken token)
			=> Volatile.Read(in _members).TryGetValue(address, out var state)
				? new(state)
				: ValueTask.FromException<ReplicaState>(new IOException());
	}
}
