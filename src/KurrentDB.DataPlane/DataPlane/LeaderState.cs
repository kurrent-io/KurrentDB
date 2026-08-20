// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.DataPlane;

using KontrolPlane;

internal sealed class LeaderState(IDatabaseStateMachine stateMachine,
	DatabaseCluster cluster,
	double renewalRate) : DatabaseState {
	private readonly TaskCompletionSource<CancellationToken> _clientBarrier = new(TaskCreationOptions.RunContinuationsAsynchronously);

	private async Task<bool> EnsureWriteBarrierAsync() {
		var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(Token);
		timeoutSource.CancelAfter(cluster.CandidateTimeout);
		try {
			// write barrier
			await stateMachine.DatabaseHandler.EnsureEpochCommittedAsync(cluster, timeoutSource.Token);

			// renew appointment
			return await stateMachine
				.KontrolPlane
				.RenewLeaderAppointmentAsync(cluster.Id, stateMachine.DatabaseHandler.CurrentNode.Address, cluster.Epoch,
					timeoutSource.Token);
		} catch {
			_clientBarrier.TrySetResult(new CancellationToken(canceled: true));
		} finally {
			timeoutSource.Dispose();
		}

		return false;
	}

	protected override async Task RunAsync() {
		Task mainTask;
		if (await EnsureWriteBarrierAsync()) {
			var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(Token);
			_clientBarrier.TrySetResult(linkedCts.Token);
			mainTask = stateMachine.DatabaseHandler.RunLeadershipAsync(stateMachine.DatabaseChanges, linkedCts.Token);
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
		} else {
			mainTask = Task.CompletedTask;
		}

		// ensure that the leadership background task is finished
		try {
			await mainTask;
		} finally {
			stateMachine.MoveToFrozenState(new(this));
		}
	}

	public override ValueTask<CancellationToken> WaitForWriteBarrierAsync(CancellationToken token)
		=> new(_clientBarrier.Task.WaitAsync(token));
}
