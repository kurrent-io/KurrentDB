// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.DataPlane;

using KontrolPlane;

internal sealed class CandidateState(
	IDatabaseStateMachine stateMachine,
	DatabaseCluster cluster) : DatabaseState {
	private readonly TaskCompletionSource<CancellationToken> _clientBarrier = new(TaskCreationOptions.RunContinuationsAsynchronously);

	protected override async Task RunAsync() {
		try {
			// write barrier
			await stateMachine.DatabaseHandler.EnsureEpochCommittedAsync(cluster, Token);

			// renew appointment
			if (await stateMachine.KontrolPlane.RenewLeaderAppointmentAsync(cluster.Id, stateMachine.DatabaseHandler.CurrentNode.Address, cluster.Epoch, Token)) {
				stateMachine.MoveToLeaderState(new(this));
			} else {
				stateMachine.MoveToFrozenState(new(this));
			}
		} catch (Exception e) {
			_clientBarrier.TrySetException(e);
			stateMachine.MoveToFrozenState(new(this));
		}
	}

	public override ValueTask<CancellationToken> WaitForWriteBarrierAsync(CancellationToken token)
		=> new(_clientBarrier.Task.WaitAsync(token));

	public LeaderState CreateLeaderState(double renewalRate)
		=> new(stateMachine, _clientBarrier, cluster.Id, cluster.Epoch, renewalRate * cluster.HeartbeatTimeout);
}
