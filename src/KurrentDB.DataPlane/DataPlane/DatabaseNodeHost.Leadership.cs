// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotNext.Threading;

namespace KurrentDB.DataPlane;

partial class DatabaseNodeHost {
	private readonly AsyncStateTracker _leaderEvent = new();
	private volatile CancellationTokenSource? _leadershipTokenSource;
	private Task _leadershipProcess;

	public async IAsyncEnumerable<LeaderAppointment> GetDatabaseLeadersAsync([EnumeratorCancellation] CancellationToken token = default) {
		// Make sure that we have an information about the database cluster
		await _clusterInfoAvailability.Task.WaitAsync(token);
		AsyncStateTracker.Token stateToken;
		do {
			stateToken = _leaderEvent.CurrentState;
			var clusterInfo = _clusterInfo;
			Debug.Assert(clusterInfo is not null);

			if (clusterInfo.LeaderNode is { } leaderNode)
				yield return new() { Leader = leaderNode, Epoch = clusterInfo.Epoch };
		} while (await _leaderEvent.WaitNextAsync(stateToken, token));
	}

	public CancellationToken LeadershipToken {
		get {
			var token = new CancellationToken(canceled: true);
			if (_leadershipTokenSource is { } cts) {
				try {
					token = cts.Token;
				} catch (ObjectDisposedException) {
					// suspend, CTS is canceled and destroyed
				}
			}

			return token;
		}
	}

	private void StartLeadership(ulong epoch, TimeSpan appointmentDuration) {

		var leadershipCts = CancellationTokenSource.CreateLinkedTokenSource(_lifecycleToken);
		_leadershipProcess = RunLeadershipAsync(epoch, appointmentDuration * _renewalRate, leadershipCts, leadershipCts.Token);
		_leadershipTokenSource = leadershipCts; // do not reorder
	}

	private async Task RunLeadershipAsync(ulong epoch, TimeSpan renewalTime, CancellationTokenSource expectedCts, CancellationToken token) {
		using var timer = new PeriodicTimer(renewalTime);
		while (await timer.WaitForNextTickAsync(token)) {
			if (!await KontrolPlane.RenewLeaderAppointmentAsync(_currentNode.DatabaseId, _currentNode.Address, epoch, token)) {
				await LeadershipLostAsync(expectedCts);
				break;
			}
		}
	}

	private async ValueTask LeadershipLostAsync(CancellationTokenSource expectedCts) {
		var newSource = new CancellationTokenSource();
		var tmp = Interlocked.CompareExchange(ref _leadershipTokenSource, expectedCts, newSource);

		// Exchange failed, dispose the constructed CTS
		if (!ReferenceEquals(tmp, expectedCts)) {
			expectedCts = newSource;
		}

		using (expectedCts) {
			await expectedCts.CancelAsync();
		}
	}

	private async ValueTask LeadershipLostAsync() {
		if (Interlocked.Exchange(ref _leadershipTokenSource, null) is { } cts) {
			using (cts) {
				await cts.CancelAsync();
			}
		}

		await _leadershipProcess.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext
		                                        | ConfigureAwaitOptions.SuppressThrowing);
	}
}
