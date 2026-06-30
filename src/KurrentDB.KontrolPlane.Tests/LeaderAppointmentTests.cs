// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Immutable;
using System.Net;
using KurrentDB.Core.XUnit.Tests;

namespace KurrentDB.KontrolPlane;

[Collection("RaftKontroller")]
public sealed class LeaderAppointmentTests : DirectoryFixture<LeaderAppointmentTests> {
	[Fact]
	public async Task AppointLeader() {
		// initialize members
		var replicaSet = new TestDataPlane();
		replicaSet.UpdateMember(TestDataPlane.Host1, new(UncommittedOffset: 100, Epoch: 1L));
		replicaSet.UpdateMember(TestDataPlane.Host2, new(UncommittedOffset: 200, Epoch: 0L));
		replicaSet.UpdateMember(TestDataPlane.Host3, new(UncommittedOffset: 150, Epoch: 1L)); // leader

		// initialize Kontroller
		await using var kontroller = new RaftKontroller(new RaftKontroller.Options {
			ListenAddress = new(IPAddress.Loopback, 3269),
			AppointmentDuration = TimeSpan.FromSeconds(1),
			ConnectionPoolCapacity = 10,
			WalOptions = new() {
				Location = Directory,
			},
			SingleNodeDeployment = true,
		}) {
			DataPlane = replicaSet,
		};

		await kontroller.StartAsync(TestToken);
		await AddNodesAsync(kontroller);

		var leader = await WaitForLeaderAsync(kontroller);
		Assert.Equal(TestDataPlane.Host3, leader.Address);
		Assert.Equal(1UL, leader.Epoch);

		await kontroller.StopAsync(TestToken);
	}

	[Fact]
	public async Task RenewLeaderAppointment() {
		var appointmentTimeout = TimeSpan.FromSeconds(1);

		// initialize members
		var replicaSet = new TestDataPlane();
		replicaSet.UpdateMember(TestDataPlane.Host1, new(UncommittedOffset: 100, Epoch: 1L));
		replicaSet.UpdateMember(TestDataPlane.Host2, new(UncommittedOffset: 200, Epoch: 0L));
		replicaSet.UpdateMember(TestDataPlane.Host3, new(UncommittedOffset: 150, Epoch: 1L)); // leader

		// initialize Kontroller
		await using var kontroller = new RaftKontroller(new RaftKontroller.Options {
			ListenAddress = new(IPAddress.Loopback, 3269),
			AppointmentDuration = appointmentTimeout,
			ConnectionPoolCapacity = 10,
			WalOptions = new() {
				Location = Directory,
			},
			SingleNodeDeployment = true,
		}) {
			DataPlane = replicaSet,
		};

		await kontroller.StartAsync(TestToken);
		await AddNodesAsync(kontroller);

		var leader = await WaitForLeaderAsync(kontroller);
		Assert.Equal(TestDataPlane.Host3, leader.Address);

		// Start renewal process in the background
		var process = new RenewalProcess(kontroller, TestDataPlane.Host3, leader.Epoch, appointmentTimeout);
		var renewalTask = process.RunAsync();

		// Make the current member as unavailable, but due to renewal process a new leader cannot be appointed
		replicaSet.RemoveMember(TestDataPlane.Host3);

		await Task.Delay(appointmentTimeout * 3, TestToken);
		leader = await WaitForLeaderAsync(kontroller);
		Assert.Equal(TestDataPlane.Host3, leader.Address);

		// Now stop renewal process and wait for a new leader appointment
		process.RequestStop();
		await renewalTask.WaitAsync(TestToken);

		do {
			leader = await WaitForLeaderAsync(kontroller);
		} while (!TestDataPlane.Host1.Equals(leader.Address));

		await kontroller.StopAsync(TestToken);
	}

	private sealed class RenewalProcess(IKontroller kontroller, EndPoint address, ulong epoch, TimeSpan appointmentTimeout) {
		private volatile bool _stopped;

		public void RequestStop() => _stopped = true;

		public async Task RunAsync() {
			while (!_stopped) {
				// Renew every 300 ms
				await Task.Delay(appointmentTimeout / 3);

				await kontroller.RenewLeaderAppointmentAsync(Database.MainDatabaseId, address, epoch, TestToken);
			}
		}
	}

	private static async Task AddNodesAsync(IKontroller kontroller) {
		var node = new DatabaseNode { DatabaseId = Database.MainDatabaseId, Address = TestDataPlane.Host1 };
		await kontroller.AddOrUpdateDatabaseNodeAsync(node, TestToken);

		node = new DatabaseNode { DatabaseId = Database.MainDatabaseId, Address = TestDataPlane.Host2 };
		await kontroller.AddOrUpdateDatabaseNodeAsync(node, TestToken);

		node = new DatabaseNode { DatabaseId = Database.MainDatabaseId, Address = TestDataPlane.Host3 };
		await kontroller.AddOrUpdateDatabaseNodeAsync(node, TestToken);

		node = new DatabaseNode { DatabaseId = Database.MainDatabaseId, Address = TestDataPlane.Host4 };
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

	private sealed class TestDataPlane : IDataPlane {
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

		public void RemoveMember(IPEndPoint endPoint) {
			for (ImmutableDictionary<EndPoint, ReplicaState> current = _members, tmp;; current = tmp) {
				var newDictionary = current.Remove(endPoint);
				tmp = Interlocked.CompareExchange(ref _members, newDictionary, current);

				if (ReferenceEquals(current, tmp))
					break;
			}
		}

		ValueTask<ReplicaState> IDataPlane.GetReplicaStateAsync(EndPoint address, CancellationToken token)
			=> Volatile.Read(in _members).TryGetValue(address, out var state)
				? new(state)
				: ValueTask.FromException<ReplicaState>(new IOException());
	}
}
