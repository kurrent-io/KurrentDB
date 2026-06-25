// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Runtime.CompilerServices;
using DotNext.Net.Cluster.Consensus.Raft;
using Kurrent.Quack;
using KurrentDB.KontrolPlane.StateMachine;

namespace KurrentDB.KontrolPlane;

using StateMachine.Queries;
using static StateMachine.LogEntries.ReplicationHelpers;

partial class RaftKontroller : IKontroller {
	public async ValueTask<IReadOnlyList<Database>> GetDatabasesAsync(CancellationToken token = default) {
		var snapshot = await _state.CaptureCurrentStateAsync(token);
		var result = new List<Database>();
		try {
			using (snapshot.RentConnection(out var connection)) {
				foreach (var database in connection.GetDatabases()) {
					result.Add(new Database { Id = database.Id, Description = database.Description, Epoch = database.Epoch });
				}
			}
		} finally {
			snapshot.Release();
		}

		return result;
	}

	public async ValueTask<IReadOnlyList<DatabaseNode>> GetDatabaseNodesAsync(string databaseId, CancellationToken token = default) {
		var snapshot = await _state.CaptureCurrentStateAsync(token);
		var result = new List<DatabaseNode>();
		try {
			using (snapshot.RentConnection(out var connection)) {
				foreach (var database in connection.GetDatabaseNodes(databaseId)) {
					result.Add(new DatabaseNode
						{ DatabaseId = databaseId, Address = database.Address, IsReadOnlyReplica = database.IsReadOnlyReplica });
				}
			}
		} finally {
			snapshot.Release();
		}

		return result;
	}

	public async ValueTask<DatabaseLeader?> GetDatabaseLeaderAsync(string databaseId, CancellationToken token = default) {
		var snapshot = await _state.CaptureCurrentStateAsync(token);
		try {
			using (snapshot.RentConnection(out var connection)) {
				return connection
					.GetDatabaseLeader(databaseId)
					.FirstOrDefault()
					.TryGet(out var leader)
					? new DatabaseLeader {
						Epoch = leader.Epoch,
						Node = new() {
							Address = leader.Address,
							DatabaseId = databaseId,
							IsReadOnlyReplica = leader.IsReadOnlyReplica
						}
					}
					: null;
			}
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
		try {
			return await _raft.RemoveDatabaseAsync(databaseId, token);
		} catch (NotLeaderException e) {
			throw new LeadershipRequiredException(e);
		}
	}

	public async ValueTask AddOrUpdateDatabaseNodeAsync(DatabaseNode node, CancellationToken token = default) {
		try {
			await _raft.AddOrUpdateDatabaseNodeAsync(node.DatabaseId, node.Address, node.IsReadOnlyReplica, token);
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

	public ValueTask<bool> RenewLeaderAppointmentAsync(string databaseId, EndPoint leaderAddress, CancellationToken token = default) {
		ValueTask<bool> task;
		try {
			task = new(RenewLeaderAppointment(databaseId, leaderAddress));
		} catch (Exception e) {
			task = ValueTask.FromException<bool>(e);
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

		static DatabaseCluster? GetDatabaseCluster(Snapshot snapshot,
			string databaseId) {
			using (snapshot.RentConnection(out var connection)) {
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
					IsReadOnlyReplica = node.IsReadOnlyReplica
				});

				if (node.IsLeader)
					leader = node.Address;
			}

			return nodes;
		}
	}
}
