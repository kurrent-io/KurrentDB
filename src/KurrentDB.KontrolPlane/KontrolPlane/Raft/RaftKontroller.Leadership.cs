// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Net.Cluster.Consensus.Raft;

namespace KurrentDB.KontrolPlane.Raft;

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
			} catch (QuorumUnreachableException) {
				// the cluster has been stopped; no leader will ever be observed again
				break;
			}

			_logger.Information("Kontroller is now a KPlane leader");

			// the local node is elected as Kontrol Plane leader
			try {
				await ProcessAppointmentsAsync(leadershipToken);
			} catch (OperationCanceledException e) when (e.CancellationToken == leadershipToken) {
				// the local node is not a leader anymore
				_logger.Information("Kontroller lost its leadership");
			} catch (Exception e) {
				_logger.Error(e, "KPlane leader encountered an error");
			}
		}
	}
}
