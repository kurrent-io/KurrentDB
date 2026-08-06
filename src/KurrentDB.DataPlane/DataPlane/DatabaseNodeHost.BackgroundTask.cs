// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Net;
using DotNext.Threading;
using KurrentDB.KontrolPlane;

namespace KurrentDB.DataPlane;

partial class DatabaseNodeHost {
	private readonly AsyncStateTracker _membershipChangeTracker = new();
	private readonly TaskCompletionSource _clusterInfoAvailability = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private Task _controlProcess;
	private volatile DatabaseCluster? _clusterInfo;

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

		var newVersion = enumerator.Current;

		_clusterInfo = newVersion;
		_clusterInfoAvailability.TrySetResult();
		await MergeClusterInfoAsync(baseline: null, newVersion);
		while (await enumerator.MoveNextAsync()) {
			newVersion = enumerator.Current;
			var oldVersion = _clusterInfo;
			_clusterInfo = newVersion;
			await MergeClusterInfoAsync(oldVersion, newVersion);
		}
	}

	private async ValueTask MergeClusterInfoAsync(DatabaseCluster? baseline, DatabaseCluster newVersion) {
		// update database leader
		if (!Equals(baseline?.LeaderAddress, newVersion.LeaderAddress))
			await ChangeDatabaseLeaderAsync(baseline?.LeaderAddress, newVersion.LeaderAddress, newVersion.Epoch, newVersion.LeaderAppointmentDuration);

		_membershipChangeTracker.TryAdvance();
	}

	private async ValueTask ChangeDatabaseLeaderAsync(EndPoint? oldLeader, EndPoint? newLeader, ulong epoch, TimeSpan appointmentDuration) {
		Debug.Assert(!Equals(oldLeader, newLeader));

		// process leadership of the current node
		switch (_currentNode.Address.Equals(oldLeader), _currentNode.Address.Equals(newLeader)) {
			case (false, true):
				// local node becomes a database leader
				StartLeadership(epoch, appointmentDuration);
				break;
			case (true, false):
				// local node is no longer a leader
				await LeadershipLostAsync();
				_leadershipProcess = Task.CompletedTask;
				break;
		}

		// Do not reorder. We want to make sure that LeadershipToken is valid at the time of the notification
		// returned by GetDatabaseLeadersAsync.
		_leaderEvent.TryAdvance();
	}

	private Task EnsureClusterInfoAvailableAsync(CancellationToken token = default)
		=> _clusterInfoAvailability.Task.WaitAsync(token);
}
