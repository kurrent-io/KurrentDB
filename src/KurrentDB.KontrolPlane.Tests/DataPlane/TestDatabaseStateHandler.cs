// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;
using DotNext.Threading;

namespace KurrentDB.DataPlane;

using KontrolPlane;

internal sealed class TestDatabaseStateHandler(DatabaseNode currentNode, ReplicaState currentState) : IDatabaseStateHandler {
	private readonly AsyncStateTracker _leaderNodeTracker = new();
	private volatile TaskCompletionSource? _leadership = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private DatabaseNode? _leaderNode;

	public async IAsyncEnumerable<DatabaseNode> GetDatabaseLeadersAsync([EnumeratorCancellation] CancellationToken token) {
		AsyncStateTracker.Token currentState;
		do {
			currentState = _leaderNodeTracker.CurrentState;
			if (_leaderNode is { } leaderNode)
				yield return leaderNode;
		} while (await _leaderNodeTracker.WaitNextAsync(currentState, token));
	}

	public Task EnsureLeadershipAsync(CancellationToken token)
		=> _leadership?.Task.WaitAsync(token) ?? Task.CompletedTask;

	Task IDatabaseStateHandler.RunReplicationAsync(Database database, DatabaseNode leaderNode, CancellationToken token) {
		_leadership = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_leaderNode = leaderNode;
		_leaderNodeTracker.TryAdvance();
		return Task.CompletedTask;
	}

	async Task IDatabaseStateHandler.RunLeadershipAsync(IAsyncEnumerable<DatabaseCluster> changes, CancellationToken token) {
		Interlocked.Exchange(ref _leadership, null)?.TrySetResult();
		await foreach (var snapshot in changes.WithCancellation(token)) {
			if (snapshot.LeaderNode is { } leaderNode) {
				_leaderNode = leaderNode;
				_leaderNodeTracker.TryAdvance();
				break;
			}
		}

		// simulate long-running work
		await token.WaitAsync();
	}

	public ValueTask<ReplicaState> GetReplicaStateAsync(CancellationToken token)
		=> ValueTask.FromResult(currentState);

	public DatabaseNode CurrentNode { get; set; } = currentNode;
}
