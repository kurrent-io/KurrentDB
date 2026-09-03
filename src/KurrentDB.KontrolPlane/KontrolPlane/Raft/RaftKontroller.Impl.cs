// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Runtime.CompilerServices;
using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Reflection;
using Kurrent.Quack;
using static System.Threading.Timeout;

namespace KurrentDB.KontrolPlane.Raft;

using StateMachine;
using StateMachine.Queries;
using static StateMachine.LogEntries.ReplicationHelpers;

partial class RaftKontroller : IKontroller, IAsyncEnumerable<EndPoint> {
	IAsyncEnumerable<EndPoint> IKontroller.Nodes => this;

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
			await _raft.AddOrUpdateDatabaseNodeAsync(node, token);
		} catch (NotLeaderException e) {
			throw new LeadershipRequiredException(e);
		}
	}

	public async ValueTask<bool> TryAddDatabaseNodeAsync(DatabaseNode node, CancellationToken token = default) {
		try {
			return await _raft.TryAddDatabaseNodeAsync(node, token);
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

	public async ValueTask<bool> RenewLeaderAppointmentAsync(string databaseId, EndPoint leaderAddress, ulong epoch, Guid instanceId, CancellationToken token = default) {
		var leadershipToken = LeadershipToken;
		try {
			// When this node becomes a Raft leader, we need to keep existing DPlane appointments
			// alive. To populate appointments, Raft leader needs some time to read information
			// from the state machine. During that period, renewal call needs to be suspended.
			await _readyToRenew.Task.WaitAsync(leadershipToken);
			return RenewLeaderAppointment(databaseId, leaderAddress, epoch, instanceId);
		} catch (OperationCanceledException e) when (e.CancellationToken == leadershipToken) {
			throw new LeadershipRequiredException(e);
		}
	}

	public async ValueTask<bool> ResignDatabaseLeaderAsync(string databaseId, ulong? epoch, CancellationToken token = default) {
		bool result;
		try {
			result = await _raft.ResignLeaderAsync(databaseId, epoch, token)
			         && _appointmentState.TryGetValue(databaseId, out var appointment)
			         && _appointmentState.TryUpdate(databaseId, appointment with { IsResigned = true }, appointment);
		} catch (NotLeaderException e) {
			throw new LeadershipRequiredException(e);
		}

		if (result) {
			_appointmentRoundSignal.Set();
		}

		return result;
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

	private DatabaseCluster? GetDatabaseCluster(ClusterState clusterState,
		string databaseId) {
		using (clusterState.RentConnection(out var connection)) {
			return connection.GetDatabase(databaseId).FirstOrDefault().TryGet(out var database)
				? new() {
					Nodes = GetDatabaseNodes(connection, databaseId, out var leaderAddress),
					LeaderAddress = leaderAddress,
					Id = databaseId,
					Epoch = database.Epoch,
					Description = database.Description,
					HeartbeatTimeout = _heartbeatTimeout,
				}
				: null;
		}

		static IReadOnlyList<DatabaseNode> GetDatabaseNodes(DuckDBAdvancedConnection connection,
			string databaseId,
			out EndPoint? leader) {
			var nodes = new List<DatabaseNode>();
			leader = null;

			foreach (var node in connection.GetDatabaseNodes(databaseId)) {
				nodes.Add(node.ToEntity(databaseId));

				if (node.IsLeader)
					leader = node.Address;
			}

			return nodes;
		}
	}

	public CancellationToken LeadershipToken => _raft.LeadershipToken;

	public async ValueTask<EndPoint> WaitForLeaderAsync(CancellationToken token = default) {
		for (;; token.ThrowIfCancellationRequested()) {
			IRaftClusterMember leader = await _raft.WaitForLeaderAsync(InfiniteTimeSpan, token);
			IReadOnlyDictionary<string, string> metadata;

			try {
				metadata = await leader.GetMetadataAsync(refresh: false, token);
			} catch {
				continue;
			}

			return GetApiEndPoint(leader.EndPoint, CreateMetadata(metadata).ApiPort);
		}
	}

	IAsyncEnumerator<EndPoint> IAsyncEnumerable<EndPoint>.GetAsyncEnumerator(CancellationToken token) {
		return Task.WhenEach(_raft.Members.Select(member => GetMemberAddressAsync(member, token)))
			.Where<Task<EndPoint>>(Task.IsCompletedSuccessfullyGetter)
			.Select(static task => task.Result)
			.GetAsyncEnumerator(token);

		static async Task<EndPoint> GetMemberAddressAsync(IRaftClusterMember member, CancellationToken token) {
			var metadata = member.Status is ClusterMemberStatus.Available
				? await member.GetMetadataAsync(refresh: false, token)
				: throw new MemberUnavailableException(member);

			return GetApiEndPoint(member.EndPoint, CreateMetadata(metadata).ApiPort);
		}
	}

	private static KontrollerMetadata CreateMetadata(IReadOnlyDictionary<string, string> metadata) {
		if (!metadata.TryGetValue(ApiPortMetadataKey, out var apiPortStringValue))
			apiPortStringValue = string.Empty;

		if (!int.TryParse(apiPortStringValue, out var apiPort))
			apiPort = 0;

		return new() { ApiPort = apiPort };
	}

	private static EndPoint GetApiEndPoint(EndPoint kontrollerNode, int apiPort) {
		return kontrollerNode switch {
			DnsEndPoint dns => new DnsEndPoint(dns.Host, apiPort),
			IPEndPoint ip => new IPEndPoint(ip.Address, apiPort),
			_ => kontrollerNode
		};
	}
}
