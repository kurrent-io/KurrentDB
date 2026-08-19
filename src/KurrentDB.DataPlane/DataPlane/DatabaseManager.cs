// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Threading;

namespace KurrentDB.DataPlane;

using KontrolPlane;

/// <summary>
/// Represents base class for Data Plane node host.
/// </summary>
public sealed partial class DatabaseManager : IAsyncEnumerable<DatabaseCluster> {
	private readonly CancellationToken _lifecycleToken; // cached to avoid ObjectDisposedException
	private readonly double _renewalRate;
	private readonly AsyncExclusiveLock _stateLock;
	private DatabaseState _state;
	private CancellationTokenSource? _lifecycleCts;

	public DatabaseManager(Options options) {
		_lifecycleCts = new();
		_lifecycleToken = _lifecycleCts.Token;
		_controlProcess = Task.CompletedTask;
		_renewalRate = options.RenewalRate;
		_stateLock = new();
		_state = new FrozenState();
		_clusterInfoChanged = new();
		_clusterInfoNullVersion = _clusterInfoChanged.CurrentState;
	}

	public async Task ResignLeader(CancellationToken token = default) {
		var tokenSource = CancellationToken.Combine([_lifecycleToken, token]);
		try {
			await KontrolPlane.ResignLeaderAsync(DatabaseHandler.CurrentNode.DatabaseId, tokenSource.Token);
		} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, _lifecycleToken)) {
			throw new ObjectDisposedException(e.Message, e);
		} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, token)) {
			throw new OperationCanceledException(e.Message, e, token);
		} finally {
			tokenSource.Dispose();
		}
	}

	/// <inheritdoc/>
	async IAsyncEnumerator<DatabaseCluster> IAsyncEnumerable<DatabaseCluster>.GetAsyncEnumerator(CancellationToken token) {
		AsyncStateTracker.Token currentState;
		do {
			currentState = _clusterInfoChanged.CurrentState;
			if (_clusterInfo is { } clusterInfo)
				yield return clusterInfo;
		} while (await _clusterInfoChanged.WaitNextAsync(currentState, token));
	}

	/// <summary>
	/// Gets or sets Kontrol Plane client.
	/// </summary>
	public required IKontrolPlane KontrolPlane { get; init; }

	/// <summary>
	/// Gets or sets database state handler.
	/// </summary>
	public required IDatabaseStateHandler DatabaseHandler {
		get;
		init;
	}

	/// <summary>
	/// Stops any incoming replication.
	/// </summary>
	/// <param name="currentEpoch">The epoch reported by the KPlane.</param>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>The state of this database node.</returns>
	internal async Task<ReplicaState> FenceAsync(ulong currentEpoch, CancellationToken token) {
		await _stateLock.AcquireAsync(token);
		try {
			if (_clusterInfo is { } clusterInfo && clusterInfo.Epoch < currentEpoch) {
				await ChangeStateAsync(new FrozenState());
				_clusterInfo = clusterInfo with { Epoch = currentEpoch };
			} else {
				// Fence is outdated, do nothing
			}
		} finally {
			_stateLock.Release();
		}

		return await DatabaseHandler.GetReplicaStateAsync(token);
	}

	public Task StartAsync(CancellationToken token) {
		Task task;
		if (token.IsCancellationRequested) {
			task = Task.FromCanceled(token);
		} else {
			task = Task.CompletedTask;
			try {
				_controlProcess = CommunicateWithKontrolPlaneAsync();
			} catch (Exception e) {
				task = Task.FromException(e);
			}
		}

		return task;
	}

	public async Task StopAsync(CancellationToken token) {
		RequestStop();
		await _stateLock.AcquireAsync(token);
		try {
			await ChangeStateAsync(new FrozenState());
		} finally {
			_stateLock.Release();
		}

		await _controlProcess.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext |
		                                      ConfigureAwaitOptions.SuppressThrowing);
		_clusterInfoChanged.TryComplete();
	}

	private void RequestStop() {
		if (Interlocked.Exchange(ref _lifecycleCts, null) is { } cts) {
			using (cts) {
				cts.Cancel();
			}
		}
	}
}
