// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.DataPlane;

using KontrolPlane;

// Leader, replicating to other nodes.
internal sealed class LeaderState(IDatabaseStateMachine stateMachine,
	DatabaseCluster cluster,
	double renewalRate) : DatabaseState {

	private async Task SendHeartbeatsAsync(CancellationTokenSource heartbeatSource) {
		var token = heartbeatSource.Token; // cached to avoid ObjectDisposedException
		using (var timer = new PeriodicTimer(cluster.HeartbeatTimeout * renewalRate)) {
			while (await stateMachine.KontrolPlane.RenewLeaderAppointmentAsync(cluster.Id,
				       stateMachine.DatabaseHandler.CurrentNode.Address, cluster.Epoch, stateMachine.DatabaseHandler.CurrentNode.InstanceId, token)) {
				await timer.WaitForNextTickAsync(token);
			}
		}

		// Renewal is rejected
		await heartbeatSource.CancelAsync();
	}

	protected override async Task RunAsync() {
		Task heartbeatTask;
		bool resignRequired;
		using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(Token)) {
			heartbeatTask = SendHeartbeatsAsync(linkedCts);
			await stateMachine
				.DatabaseHandler
				.RunLeadershipAsync(cluster, stateMachine.DatabaseChanges, linkedCts.Token)
				.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext
				                | ConfigureAwaitOptions.SuppressThrowing);

			if (linkedCts.IsCancellationRequested) {
				// Canceled by the state machine or heartbeat loop
				resignRequired = false;
			} else {
				// Leadership is finished because RunLeadershipAsync stops, send Resign to KPlane.
				// This step is necessary when DPlane Leader has normal communication with KPlane
				// to renew its leadership, but it can't replicate to the quorum (due to
				// network partitioning).
				resignRequired = true;
				await linkedCts.CancelAsync();
			}
		}

		await heartbeatTask.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext
		                                   | ConfigureAwaitOptions.SuppressThrowing);

		var callerState = new WeakReference<DatabaseState>(this);
		if (resignRequired) {
			stateMachine.MoveToFrozenState(callerState, cluster.Id, cluster.Epoch);
		} else {
			stateMachine.MoveToFrozenState(callerState);
		}
	}
}
