// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;

namespace KurrentDB.KontrolPlane;

partial class RaftKontroller {
	private readonly CancellationToken _lifecycleToken; // cached to avoid ObjectDisposedException
	private volatile CancellationTokenSource? _lifecycleTokenSource;

	private async Task HandleLeadershipAsync() {
		for (;;) {
			CancellationToken leadershipToken;
			try {
				leadershipToken = await _raft.WaitForLeadershipAsync(_lifecycleToken);
			} catch (OperationCanceledException e) when (e.CancellationToken == _lifecycleToken) {
				break;
			} catch (ObjectDisposedException) {
				break;
			}

			// the local node is elected as Kontrol Plane leader
			try {
				await ProcessAppointmentsAsync(leadershipToken);
			} catch (OperationCanceledException e) when (e.CancellationToken == leadershipToken) {
				// the local node is not a leader anymore
			}
		}
	}
}
