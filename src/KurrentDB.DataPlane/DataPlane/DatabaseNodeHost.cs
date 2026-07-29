// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.DataPlane;

using KontrolPlane;

/// <summary>
/// Represents base class for Data Plane node host.
/// </summary>
public abstract partial class DatabaseNodeHost {
	private readonly CancellationToken _lifecycleToken; // cached to avoid ObjectDisposedException
	private readonly TimeSpan _pollingPeriod;
	private readonly DatabaseNode _currentNode;
	private readonly double _renewalRate;
	private CancellationTokenSource? _lifecycleCts;

	protected DatabaseNodeHost(Options options) {
		_lifecycleCts = new();
		_lifecycleToken = _lifecycleCts.Token;
		_pollingPeriod = options.PollingPeriod;
		_controlProcess = _leadershipProcess = Task.CompletedTask;
		_currentNode = options.CurrentNode;
		_renewalRate = options.RenewalRate;
	}

	/// <summary>
	/// Gets or sets Kontrol Plane client.
	/// </summary>
	public required IKontrolPlane KontrolPlane { get; init; }

	/// <summary>
	/// Gets the replication state of the database node.
	/// </summary>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>The state of this database node.</returns>
	protected internal abstract ValueTask<ReplicaState> GetReplicaStateAsync(CancellationToken token);

	public Task StartAsync(CancellationToken token) {
		Task task;

		if (token.IsCancellationRequested) {
			task = Task.FromCanceled(token);
		} else {
			_controlProcess = CommunicateWithKontrolPlaneAsync();
			task = Task.CompletedTask;
		}

		return task;
	}

	public async Task StopAsync(CancellationToken token) {
		RequestStop();
		await _controlProcess.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext |
		                                      ConfigureAwaitOptions.SuppressThrowing);
	}

	public abstract ValueTask LeadershipStartedAsync(ulong epoch, CancellationToken token);

	private void RequestStop() {
		if (Interlocked.Exchange(ref _lifecycleCts, null) is { } cts) {
			using (cts) {
				cts.Cancel();
			}
		}
	}
}
