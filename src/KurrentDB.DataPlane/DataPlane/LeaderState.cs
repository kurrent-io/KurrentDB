// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.DataPlane;

using KontrolPlane;

internal sealed class LeaderState(IDatabaseStateMachine stateMachine,
	DatabaseCluster cluster,
	double renewalRate) : DatabaseState {

	protected override async Task RunAsync() {
		var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(Token);
		var mainTask = stateMachine.DatabaseHandler.RunLeadershipAsync(stateMachine.DatabaseChanges, linkedCts.Token);
		var timer = new PeriodicTimer(cluster.HeartbeatTimeout * renewalRate);
		try {
			while (await stateMachine.KontrolPlane.RenewLeaderAppointmentAsync(cluster.Id,
				       stateMachine.DatabaseHandler.CurrentNode.Address, cluster.Epoch, linkedCts.Token)) {
				await timer.WaitForNextTickAsync(linkedCts.Token);
			}

			await linkedCts.CancelAsync();
		} catch (OperationCanceledException) {
			// suppress exception
		} finally {
			timer.Dispose();
			linkedCts.Dispose();
		}

		// ensure that the leadership background task is finished
		try {
			await mainTask;
		} finally {
			stateMachine.MoveToFrozenState(new(this));
		}
	}
}
