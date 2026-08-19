// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.DataPlane;

internal sealed class LeaderState(IDatabaseStateMachine stateMachine,
	TaskCompletionSource<CancellationToken> clientBarrier,
	string databaseId,
	ulong epoch,
	TimeSpan heartbeatTimeout) : DatabaseState {

	protected override async Task RunAsync() {
		clientBarrier.TrySetResult(Token);
		var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(Token);
		var mainTask = stateMachine.DatabaseHandler.RunLeadershipAsync(stateMachine.DatabaseChanges, linkedCts.Token);
		var timer = new PeriodicTimer(heartbeatTimeout);
		try {
			while (await stateMachine.KontrolPlane.RenewLeaderAppointmentAsync(databaseId, stateMachine.DatabaseHandler.CurrentNode.Address, epoch, linkedCts.Token)) {
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

	public override ValueTask<CancellationToken> WaitForWriteBarrierAsync(CancellationToken token)
		=> ValueTask.FromResult(Token);
}
