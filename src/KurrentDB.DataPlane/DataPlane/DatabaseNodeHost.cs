// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotNext.Threading;

namespace KurrentDB.DataPlane;

using KontrolPlane;

/// <summary>
/// Represents base class for Data Plane node host.
/// </summary>
public abstract partial class DatabaseNodeHost : IDatabaseNode {
	private readonly CancellationToken _lifecycleToken; // cached to avoid ObjectDisposedException
	private readonly TimeSpan _pollingPeriod;
	private readonly double _renewalRate;
	private readonly DatabaseNode _currentNode;
	private CancellationTokenSource? _lifecycleCts;

	protected DatabaseNodeHost(Options options) {
		_lifecycleCts = new();
		_lifecycleToken = _lifecycleCts.Token;
		_pollingPeriod = options.PollingPeriod;
		_controlProcess = _leadershipProcess = Task.CompletedTask;
		_currentNode = options.CurrentNode;
		_renewalRate = options.RenewalRate;
	}

	public async Task ResignLeader(CancellationToken token = default) {
		var tokenSource = CancellationToken.Combine([_lifecycleToken, token]);
		try {
			await KontrolPlane.ResignLeaderAsync(_currentNode.DatabaseId, tokenSource.Token);
		} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, _lifecycleToken)) {
			throw new ObjectDisposedException(e.Message, e);
		} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, token)) {
			throw new OperationCanceledException(e.Message, e, token);
		} finally {
			tokenSource.Dispose();
		}
	}

	public DatabaseNode CurrentNode => _clusterInfo?[_currentNode.Address] ?? _currentNode;

	public ValueTask<DatabaseCluster> GetDatabaseInfoAsync(CancellationToken token = default)
		=> _clusterInfoAvailability.Task.IsCompleted ? ValueTask.FromResult(_clusterInfo!) : WaitForClusterInfoAsync(token);

	private async ValueTask<DatabaseCluster> WaitForClusterInfoAsync(CancellationToken token = default) {
		await EnsureClusterInfoAvailableAsync(token);

		Debug.Assert(_clusterInfo is not null);
		return _clusterInfo;
	}

	public async IAsyncEnumerable<IReadOnlySet<DatabaseNode>> GetDatabaseMembershipChangesAsync(
		[EnumeratorCancellation] CancellationToken token = default) {
		using var tokenSource = CancellationToken.Combine([_lifecycleToken, token]);

		try {
			await EnsureClusterInfoAvailableAsync(token);
		} catch (ObjectDisposedException) {
			yield break;
		}

		Debug.Assert(_clusterInfo is not null);

		var stateToken = _membershipChangeTracker.CurrentState;
		ImmutableHashSet<DatabaseNode> baseline = [.. _clusterInfo.Nodes];
		yield return baseline;

		for (ImmutableHashSet<DatabaseNode> newVersion;; baseline = newVersion) {
			bool loopAlive;
			try {
				loopAlive = await _membershipChangeTracker.WaitNextAsync(stateToken, token);
			} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, _lifecycleToken)) {
				loopAlive = false;
			} catch (OperationCanceledException e) when (e.CancellationToken == tokenSource.Token) {
				throw new OperationCanceledException(e.Message, e, tokenSource.CancellationOrigin);
			}

			if (!loopAlive)
				break;

			newVersion = [.. _clusterInfo.Nodes];
			if (!newVersion.SetEquals(baseline)) {
				yield return newVersion;
			}
		}
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
		_membershipChangeTracker.TryComplete();
		_clusterInfoAvailability.TrySetException(new ObjectDisposedException(GetType().Name));
	}

	private void RequestStop() {
		if (Interlocked.Exchange(ref _lifecycleCts, null) is { } cts) {
			using (cts) {
				cts.Cancel();
			}
		}
	}
}
