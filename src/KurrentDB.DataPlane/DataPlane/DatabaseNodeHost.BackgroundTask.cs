// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

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
		await ChangeDatabaseLeaderAsync(baseline, newVersion);

		_membershipChangeTracker.TryAdvance();
	}

	private async ValueTask ChangeDatabaseLeaderAsync(DatabaseCluster? baseline, DatabaseCluster newVersion) {
		// update database leader: either the leader address changed, or the same leader was
		// re-appointed under a new epoch (KPlane can do this, e.g. right after its own failover,
		// without ever reporting an intermediate LeaderAddress change).
		switch (_currentNode.Address.Equals(baseline?.LeaderAddress), _currentNode.Address.Equals(newVersion.LeaderAddress)) {
			case (false, true):
				// local node becomes a database leader
				StartLeadership(newVersion.Epoch, newVersion.LeaderAppointmentDuration);
				break;
			case (true, false):
				// local node is no longer a leader
				await LeadershipLostAsync();
				_leadershipProcess = Task.CompletedTask;
				break;
			case (true, true) when baseline!.Epoch != newVersion.Epoch:
				// still the leader, but re-appointed under a new epoch: restart the leadership session
				// so the renewal loop is guaranteed to use the epoch this appointment actually belongs to.
				await LeadershipLostAsync();
				StartLeadership(newVersion.Epoch, newVersion.LeaderAppointmentDuration);
				break;
		}

		// Do not reorder. We want to make sure that LeadershipToken is valid at the time of the notification
		// returned by GetDatabaseLeadersAsync.
		_leaderEvent.TryAdvance();
	}

	private Task EnsureClusterInfoAvailableAsync(CancellationToken token = default)
		=> _clusterInfoAvailability.Task.WaitAsync(token);
}
