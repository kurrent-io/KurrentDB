// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using DotNext.Threading;
using Serilog;

namespace KurrentDB.DataPlane;

using KontrolPlane;

/// <summary>
/// Represents base class for Data Plane node host.
/// </summary>
public sealed partial class DatabaseManager : IAsyncEnumerable<DatabaseCluster> {
	private readonly CancellationToken _lifecycleToken; // cached to avoid ObjectDisposedException
	private readonly double _renewalRate;
	private readonly AsyncExclusiveLock _stateLock;
	private readonly ILogger _logger;
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
		_logger = Log.ForContext<DatabaseManager>();
	}

	public async Task<bool> ResignLeader(CancellationToken token = default) {
		var tokenSource = CancellationToken.Combine([_lifecycleToken, token]);
		try {
			return await KontrolPlane.ResignLeaderAsync(
				DatabaseHandler.CurrentNode.DatabaseId,
				epoch: null,
				tokenSource.Token);
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
		init {
			field = value;
			_logger = _logger.ForContext("DPlaneNode", value.CurrentNode.Address.ToString());
		}
	}

	/// <summary>
	/// Stops any incoming & outgoing replication.
	/// </summary>
	/// <param name="currentEpoch">The epoch reported by the KPlane.</param>
	/// <param name="token">The token that can be used to cancel the operation.</param>
	/// <returns>The state of this database node.</returns>
	internal async Task<ReplicaState> FenceAsync(ulong currentEpoch, CancellationToken token) {
		var advanced = false;
		await _stateLock.AcquireAsync(token);
		try {
			if (_clusterInfo is { } clusterInfo && clusterInfo.Epoch < currentEpoch) {
				// Route the epoch bump through the same transition logic the KPlane stream uses, instead of
				// mutating _clusterInfo directly: otherwise, once the stream later delivers a snapshot with
				// this same (already-applied) epoch, ChangeDatabaseLeaderAsync sees baseline.Epoch == newVersion.Epoch
				// and treats it as a no-op, leaving a re-appointed leader stuck in the FrozenState this fence forced it into.
				var fencedVersion = clusterInfo with { Epoch = currentEpoch, LeaderAddress = null };
				_clusterInfo = fencedVersion;
				await ChangeStateAsync(new FrozenState());
				advanced = true;
			} else {
				// Fence is outdated, do nothing
			}
		} finally {
			_stateLock.Release();
		}

		if (advanced)
			_clusterInfoChanged.TryAdvance();

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
