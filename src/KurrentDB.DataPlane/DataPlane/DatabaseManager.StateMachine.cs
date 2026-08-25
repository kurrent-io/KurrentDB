// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;

namespace KurrentDB.DataPlane;

using KontrolPlane;

partial class DatabaseManager : IDatabaseStateMachine {
	private async ValueTask ChangeStateAsync(DatabaseCluster newVersion) {
		bool advanced;
		await _stateLock.AcquireAsync(_lifecycleToken);
		try {
			// Ignore any information with stale epoch
			advanced = _clusterInfo is null || _clusterInfo.Epoch <= newVersion.Epoch;
			if (advanced) {
				await ChangeStateAsync(_clusterInfo, newVersion);
			}

			_clusterInfo = newVersion;
		} finally {
			_stateLock.Release();
		}

		if (advanced) {
			_clusterInfoChanged.TryAdvance();
		}
	}

	private async ValueTask ChangeStateAsync(DatabaseCluster? baseline, DatabaseCluster newVersion) {
		var currentNode = DatabaseHandler.CurrentNode;
		if (newVersion[currentNode.Address] is { } newCurrentNode) {
			// update information about the current node if needed
			if (currentNode != newCurrentNode)
				DatabaseHandler.CurrentNode = newCurrentNode with { InstanceId = currentNode.InstanceId };
		} else if (_state is not FrozenState) {
			// current node is removed from the cluster configuration, move to frozen state
			await ChangeStateAsync(new FrozenState());
			return;
		}

		await ChangeDatabaseLeaderAsync(baseline, newVersion, currentNode);
	}

	private async ValueTask ChangeDatabaseLeaderAsync(DatabaseCluster? baseline, DatabaseCluster newVersion, DatabaseNode currentNode) {
		var oldLeader = baseline?.LeaderAddress;
		var newLeader = newVersion.LeaderAddress;

		// update database leader: either the leader address changed, or the same leader was
		// re-appointed under a new epoch (KPlane can do this, e.g. right after its own failover,
		// without ever reporting an intermediate LeaderAddress change).
		DatabaseState newState;
		switch (currentNode.Address.Equals(oldLeader), currentNode.Address.Equals(newLeader)) {
			case (false, true) when currentNode.InstanceId == newVersion.LeaderNode?.InstanceId:
				// local node becomes a database leader
				await ChangeStateAsync(newState = new LeaderState(this, newVersion, _renewalRate));
				break;
			case (true, false):
				// local node is no longer a leader
				await ChangeStateAsync(newState = newLeader is null
					? new FrozenState()
					: new FollowerState(DatabaseHandler, newVersion));
				break;
			case (true, true) when baseline is not null && baseline.Epoch != newVersion.Epoch:
				// still the leader, but re-appointed under a new epoch: restart the leadership session
				// so the renewal loop is guaranteed to use the epoch this appointment actually belongs to.
				await ChangeStateAsync(newState = new LeaderState(this, newVersion, _renewalRate));
				break;
			case (false, false) when newLeader is null && _state is not FrozenState:
				// Leader is not known to the current node
				await ChangeStateAsync(newState = new FrozenState());
				break;
			case (false, false) when newLeader is not null && !newLeader.Equals(oldLeader):
				// Leader is changed, re-enter Follower state
				await ChangeStateAsync(newState = new FollowerState(DatabaseHandler, newVersion));
				break;
			default:
				return;
		}

		newState.TryStart();
	}

	private ValueTask ChangeStateAsync(DatabaseState newState) {
		Debug.Assert(_stateLock.IsLockHeld);

		return Interlocked.Exchange(ref _state, newState).DisposeAsync();
	}

	private async void MoveToFrozenState(TransitionToResigningState transition) {
		var lockTaken = false;
		try {
			await _stateLock.AcquireAsync(_lifecycleToken);
			lockTaken = true;

			if (transition.IsValid(_state)) {
				await ChangeStateAsync(new ResigningState(KontrolPlane, transition.DatabaseId, transition.CurrentEpoch));
			}
		} catch {
			// we can't throw here, it's async void method
		} finally {
			if (lockTaken)
				_stateLock.Release();
		}
	}

	void IDatabaseStateMachine.MoveToFrozenState(WeakReference<DatabaseState> callerState, string databaseId, ulong currentEpoch)
		=> ThreadPool.UnsafeQueueUserWorkItem(MoveToFrozenState, new TransitionToResigningState(callerState, databaseId, currentEpoch), preferLocal: false);

	private async void MoveToFrozenState(WeakReference<DatabaseState> callerState) {
		var lockTaken = false;
		try {
			await _stateLock.AcquireAsync(_lifecycleToken);
			lockTaken = true;

			if (callerState.TryGetTarget(out var currentState) && ReferenceEquals(currentState, _state)) {
				await ChangeStateAsync(new FrozenState());
			}
		} catch {
			// we can't throw here, it's async void method
		} finally {
			if (lockTaken)
				_stateLock.Release();
		}
	}

	void IDatabaseStateMachine.MoveToFrozenState(WeakReference<DatabaseState> callerState)
		=> ThreadPool.UnsafeQueueUserWorkItem(MoveToFrozenState, callerState, preferLocal: false);

	IAsyncEnumerable<DatabaseCluster> IDatabaseStateMachine.DatabaseChanges => this;

	private sealed class TransitionToResigningState(WeakReference<DatabaseState> callerState, string databaseId, ulong currentEpoch) {
		public bool IsValid(DatabaseState currentState)
			=> callerState.TryGetTarget(out var strongRef) && ReferenceEquals(strongRef, currentState);

		public string DatabaseId => databaseId;

		public ulong CurrentEpoch => currentEpoch;
	}
}
