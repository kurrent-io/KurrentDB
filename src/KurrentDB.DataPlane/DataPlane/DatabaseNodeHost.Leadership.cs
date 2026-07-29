// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;

namespace KurrentDB.DataPlane;

using KontrolPlane;

partial class DatabaseNodeHost {
	private Task _leadershipProcess;
	private CancellationTokenSource? _leadershipCts;

	private void StartLeadership(TimeSpan appointmentDuration, ulong epoch) {
		Debug.Assert(_leadershipCts is null);

		_leadershipCts = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleToken);
		_leadershipProcess = RunLeadershipAsync(appointmentDuration * _renewalRate, _leadershipCts.Token);
	}

	private async Task RunLeadershipAsync(TimeSpan renewalTime, CancellationToken token) {

	}
}
