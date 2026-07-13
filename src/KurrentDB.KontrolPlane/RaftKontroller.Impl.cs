// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Runtime.CompilerServices;
using DotNext.Net.Cluster.Consensus.Raft;
using Kurrent.Quack;
using KurrentDB.KontrolPlane.StateMachine;
using static System.Threading.Timeout;

namespace KurrentDB.KontrolPlane;

using StateMachine.Queries;
using static StateMachine.LogEntries.ReplicationHelpers;

partial class RaftKontroller : IKontroller {
	public async ValueTask<IReadOnlySet<string>> GetDatabasesAsync(CancellationToken token = default) {
		var result = new HashSet<string>();
		var snapshot = await _state.CaptureCurrentStateAsync(token);
		try {
			using (snapshot.RentConnection(out var connection)) {
				foreach (var databaseId in connection.GetDatabases()) {
					result.Add(databaseId);
				}
			}
		} finally {
			snapshot.Release();
		}

		return result;
	}

	public async ValueTask<DatabaseCluster?> GetDatabaseAsync(string databaseId, CancellationToken token = default) {
		var snapshot = await _state.CaptureCurrentStateAsync(token);
		try {
			return GetDatabaseCluster(snapshot, databaseId);
		} finally {
			snapshot.Release();
		}
	}

	public async ValueTask AddOrUpdateDatabaseAsync(Database database, CancellationToken token = default) {
		try {
			await _raft.AddOrUpdateDatabaseAsync(database.Id, database.Description, token);
		} catch (NotLeaderException e) {
			throw new LeadershipRequiredException(e);
		}
	}

	public async ValueTask<bool> RemoveDatabaseAsync(string databaseId, CancellationToken token = default) {
		if (databaseId is Database.MainDatabaseId)
			throw new ArgumentException($"Built-in '{Database.MainDatabaseId}' database cannot be removed.", nameof(databaseId));

		try {
			return await _raft.RemoveDatabaseAsync(databaseId, token);
		} catch (NotLeaderException e) {
			throw new LeadershipRequiredException(e);
		}
	}

	public async ValueTask AddOrUpdateDatabaseNodeAsync(DatabaseNode node, CancellationToken token = default) {
		try {
			await _raft.AddOrUpdateDatabaseNodeAsync(node.DatabaseId, node.Address, node.Role, token);
		} catch (NotLeaderException e) {
			throw new LeadershipRequiredException(e);
		}
	}

	public async ValueTask<bool> RemoveDatabaseNodeAsync(string databaseId, EndPoint address, CancellationToken token = default) {
		try {
			return await _raft.RemoveDatabaseNodeAsync(databaseId, address, token);
		} catch (NotLeaderException e) {
			throw new LeadershipRequiredException(e);
		}
	}

	public ValueTask<bool> RenewLeaderAppointmentAsync(string databaseId, EndPoint leaderAddress, ulong epoch, CancellationToken token = default) {
		ValueTask<bool> task;
		if (token.IsCancellationRequested) {
			task = ValueTask.FromCanceled<bool>(token);
		} else {
			try {
				task = new(RenewLeaderAppointment(databaseId, leaderAddress, epoch));
			} catch (Exception e) {
				task = ValueTask.FromException<bool>(e);
			}
		}

		return task;
	}

	public async IAsyncEnumerable<DatabaseCluster> ListenDatabaseAsync(string databaseId, [EnumeratorCancellation] CancellationToken token = default) {
		await foreach (var snapshot in _state.TrackChangesAsync(databaseId, token)) {
			try {
				if (GetDatabaseCluster(snapshot, databaseId) is { } cluster) {
					yield return cluster;
				} else {
					break;
				}
			} finally {
				snapshot.Release();
			}
		}
	}

	private static DatabaseCluster? GetDatabaseCluster(ClusterState clusterState,
		string databaseId) {
		using (clusterState.RentConnection(out var connection)) {
			return connection.GetDatabase(databaseId).FirstOrDefault().TryGet(out var database)
				? new() {
					Nodes = GetDatabaseNodes(connection, databaseId, out var leaderAddress),
					LeaderAddress = leaderAddress,
					Id = databaseId,
					Epoch = database.Epoch,
					Description = database.Description
				}
				: null;
		}

		static IReadOnlyList<DatabaseNode> GetDatabaseNodes(DuckDBAdvancedConnection connection,
			string databaseId,
			out EndPoint? leader) {
			var nodes = new List<DatabaseNode>();
			leader = null;

			foreach (var node in connection.GetDatabaseNodes(databaseId)) {
				nodes.Add(new() {
					Address = node.Address,
					DatabaseId = databaseId,
					Role = node.Role
				});

				if (node.IsLeader)
					leader = node.Address;
			}

			return nodes;
		}
	}

	public CancellationToken LeadershipToken => _raft.LeadershipToken;

	public async ValueTask<EndPoint> WaitForLeaderAsync(CancellationToken token = default)
		=> (await _raft.WaitForLeaderAsync(InfiniteTimeSpan, token)).EndPoint;
}
