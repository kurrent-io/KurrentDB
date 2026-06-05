// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Runtime.CompilerServices;
using DotNext.Net.Cluster.Consensus.Raft;

namespace KurrentDB.KontrolPlane;

using static StateMachine.LogEntries.ReplicationHelpers;

partial class RaftKontroller : IKontroller {
	public IReadOnlyDictionary<string, IDatabase> Databases => _state.CurrentState;

	public async ValueTask<bool> AddOrUpdateDatabaseAsync(string databaseId, string description = "", CancellationToken token = default) {
		var completion = new TaskCompletionSource<bool>();
		try {
			await _raft.AddOrUpdateDatabaseAsync(databaseId, description, completion, token);
		} catch (NotLeaderException e) {
			throw new LeadershipRequiredException(e);
		}

		return await completion.Task.WaitAsync(token);
	}

	public async ValueTask<bool> RemoveDatabaseAsync(string databaseId, CancellationToken token = default) {
		var completion = new TaskCompletionSource<bool>();
		try {
			await _raft.RemoveDatabaseAsync(databaseId, completion, token);
		} catch (NotLeaderException e) {
			throw new LeadershipRequiredException(e);
		}

		return await completion.Task.WaitAsync(token);
	}

	public async ValueTask<bool> AddOrUpdateDatabaseNodeAsync(string databaseId, IDatabaseNode node, CancellationToken token = default) {
		var completion = new TaskCompletionSource<bool>();
		try {
			await _raft.AddOrUpdateDatabaseNodeAsync(databaseId, node, completion, token);
		} catch (NotLeaderException e) {
			throw new LeadershipRequiredException(e);
		}

		return await completion.Task.WaitAsync(token);
	}

	public async ValueTask<bool> RemoveDatabaseNodeAsync(string databaseId, EndPoint address, CancellationToken token = default) {
		var completion = new TaskCompletionSource<bool>();
		try {
			await _raft.RemoveDatabaseNodeAsync(databaseId, address, completion, token);
		} catch (NotLeaderException e) {
			throw new LeadershipRequiredException(e);
		}

		return await completion.Task.WaitAsync(token);
	}

	private async ValueTask<bool> AppointLeaderAsync(string databaseId,
		EndPoint address,
		ulong expectedVersion,
		CancellationToken token = default) {
		var completion = new TaskCompletionSource<bool>();
		await _raft.AppointLeaderAsync(databaseId, address, expectedVersion, completion, token);
		return await completion.Task.WaitAsync(token);
	}

	public ValueTask<bool> RenewLeaderAppointmentAsync(string databaseId, EndPoint leaderAddress, CancellationToken token = default) {
		ValueTask<bool> task;
		try {
			task = new(RenewLeaderAppointment(databaseId, leaderAddress));
		} catch (Exception e) {
			task = ValueTask.FromException<bool>(e);
		}

		return task;
	}

	public async IAsyncEnumerable<DatabaseSnapshot> ListenDatabaseAsync(string databaseId, [EnumeratorCancellation] CancellationToken token = default) {
		yield break;
		// var leadershiptToken = _raft.LeadershipToken;
		// var tokenSource = _multiplexer.Combine(_raft.LeadershipToken, token);
		// try {
		// 	yield return null;
		// } catch (OperationCanceledException e) when (e.CausedBy(tokenSource, leadershiptToken)) {
		// 	throw new LeadershipRequiredException(e);
		// } catch (OperationCanceledException e) when (e.CancellationToken == tokenSource.CancellationOrigin) {
		// 	throw new OperationCanceledException(e.Message, e, e.CancellationToken);
		// } finally {
		// 	await tokenSource.DisposeAsync();
		// }
	}
}
