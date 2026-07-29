// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Net;
using KurrentDB.KontrolPlane;

namespace KurrentDB.DataPlane;

partial class DatabaseNodeHost {
	private Task _controlProcess;

	private async Task CommunicateWithKontrolPlaneAsync() {
		var timer = new PeriodicTimer(_pollingPeriod);
		try {
			// Control loop provides communication with Kontrol Plane nodes
			do {
				await AnnounceCurrentNodeAsync();
			} while (await timer.WaitForNextTickAsync(_lifecycleToken));
		} finally {
			timer.Dispose();
		}
	}

	private async Task AnnounceCurrentNodeAsync() {
		await using var enumerator = KontrolPlane
			.AnnounceNodeAsync(_currentNode, _lifecycleToken)
			.GetAsyncEnumerator();

		if (!await enumerator.MoveNextAsync())
			return;

		for (var baseline = new DatabaseClusterSnapshot(enumerator.Current); await enumerator.MoveNextAsync();) {
			await MergeClusterInfoAsync(baseline, enumerator.Current);
		}
	}

	private async ValueTask MergeClusterInfoAsync(DatabaseClusterSnapshot baseline, DatabaseCluster newVersion) {
		baseline.LeaderAppointmentDuration = newVersion.LeaderAppointmentDuration;

		baseline.Clear();

		if (!Equals(baseline.LeaderAddress, newVersion.LeaderAddress))
			await ChangeDatabaseLeaderAsync(baseline, newVersion.LeaderAddress, newVersion.Epoch);
	}

	private async ValueTask ChangeDatabaseLeaderAsync(DatabaseClusterSnapshot baseline, EndPoint? leaderAddress, ulong epoch) {
		Debug.Assert(!Equals(baseline.LeaderAddress, leaderAddress));

		baseline.LeaderAddress = leaderAddress;
		if (_currentNode.Address.Equals(leaderAddress)) {
			// local node becomes a database leader
			StartLeadership(baseline.LeaderAppointmentDuration, epoch);
		} else if (Equals(baseline.LeaderAddress, _currentNode.Address)) {
			// local node is no longer a leader
		}
	}

	private sealed class DatabaseClusterSnapshot : Dictionary<EndPoint, DatabaseNode> {
		public DatabaseClusterSnapshot(DatabaseCluster clusterInfo) {
			LeaderAddress = clusterInfo.LeaderAddress;
			LeaderAppointmentDuration = clusterInfo.LeaderAppointmentDuration;

			foreach (var databaseNode in clusterInfo.Nodes) {
				Add(databaseNode.Address, databaseNode);
			}
		}

		public EndPoint? LeaderAddress { get; set; }

		public TimeSpan LeaderAppointmentDuration { get; set; }
	}
}
