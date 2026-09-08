// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Net;
using System.Runtime.CompilerServices;
using DotNext.Collections.Generic;
using DotNext.Net.Cluster;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Threading;
using Kurrent.Quack;
using static System.Threading.Timeout;

namespace KurrentDB.KontrolPlane.Raft;

using StateMachine;
using StateMachine.Queries;
using static StateMachine.LogEntries.ReplicationHelpers;

partial class RaftKontroller : IKontroller, IAsyncEnumerable<EndPoint> {
	private static readonly Task<EndPoint?> NoEndPointTask = Task.FromResult<EndPoint?>(null);

	IAsyncEnumerable<EndPoint> IKontroller.Nodes => this;

	public async ValueTask<IReadOnlySet<string>> GetDatabasesAsync(CancellationToken token = default) {
		var result = new HashSet<string>();
		var tokenSource = _multiplexer.Combine(token, _lifecycleToken);
		var snapshot = default(ClusterState);
		try {
			snapshot = await _state.CaptureCurrentStateAsync(tokenSource.Token);
			using (snapshot.RentConnection(out var connection)) {
				foreach (var databaseId in connection.GetDatabases()) {
					result.Add(databaseId);
				}
			}
		} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, _lifecycleToken)) {
			throw new ObjectDisposedException(e.Message, e);
		} catch (OperationCanceledException e) when (e.CancellationToken == tokenSource.Token) {
			throw new OperationCanceledException(e.Message, e, tokenSource.CancellationOrigin);
		} finally {
			snapshot?.Release();
			await tokenSource.DisposeAsync();
		}

		return result;
	}

	public async ValueTask<DatabaseCluster?> GetDatabaseAsync(string databaseId, CancellationToken token = default) {
		var tokenSource = _multiplexer.Combine(token, _lifecycleToken);
		var snapshot = default(ClusterState);
		try {
			snapshot = await _state.CaptureCurrentStateAsync(tokenSource.Token);
			return GetDatabaseCluster(snapshot, databaseId);
		} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, _lifecycleToken)) {
			throw new ObjectDisposedException(e.Message, e);
		} catch (OperationCanceledException e) when (e.CancellationToken == tokenSource.Token) {
			throw new OperationCanceledException(e.Message, e, tokenSource.CancellationOrigin);
		} finally {
			snapshot?.Release();
			await tokenSource.DisposeAsync();
		}
	}

	public async ValueTask AddOrUpdateDatabaseAsync(Database database, CancellationToken token = default) {
		var tokenSource = _multiplexer.Combine(token, _lifecycleToken);
		try {
			await _raft.AddOrUpdateDatabaseAsync(database.Id, database.Description, tokenSource.Token);
		} catch (NotLeaderException e) {
			throw new LeadershipRequiredException(e);
		} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, _lifecycleToken)) {
			throw new ObjectDisposedException(e.Message, e);
		} catch (OperationCanceledException e) when (e.CancellationToken == tokenSource.Token) {
			throw new OperationCanceledException(e.Message, e, tokenSource.CancellationOrigin);
		} finally {
			await tokenSource.DisposeAsync();
		}
	}

	public async ValueTask<bool> RemoveDatabaseAsync(string databaseId, CancellationToken token = default) {
		if (databaseId is Database.MainDatabaseId)
			throw new ArgumentException($"Built-in '{Database.MainDatabaseId}' database cannot be removed.", nameof(databaseId));

		var tokenSource = _multiplexer.Combine(token, _lifecycleToken);
		try {
			return await _raft.RemoveDatabaseAsync(databaseId, tokenSource.Token);
		} catch (NotLeaderException e) {
			throw new LeadershipRequiredException(e);
		} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, _lifecycleToken)) {
			throw new ObjectDisposedException(e.Message, e);
		} catch (OperationCanceledException e) when (e.CancellationToken == tokenSource.Token) {
			throw new OperationCanceledException(e.Message, e, tokenSource.CancellationOrigin);
		} finally {
			await tokenSource.DisposeAsync();
		}
	}

	public async ValueTask AddOrUpdateDatabaseNodeAsync(DatabaseNode node, CancellationToken token = default) {
		var tokenSource = _multiplexer.Combine(token, _lifecycleToken);
		try {
			await _raft.AddOrUpdateDatabaseNodeAsync(node, tokenSource.Token);
		} catch (NotLeaderException e) {
			throw new LeadershipRequiredException(e);
		} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, _lifecycleToken)) {
			throw new ObjectDisposedException(e.Message, e);
		} catch (OperationCanceledException e) when (e.CancellationToken == tokenSource.Token) {
			throw new OperationCanceledException(e.Message, e, tokenSource.CancellationOrigin);
		} finally {
			await tokenSource.DisposeAsync();
		}
	}

	public async ValueTask<bool> TryAddDatabaseNodeAsync(DatabaseNode node, CancellationToken token = default) {
		var tokenSource = _multiplexer.Combine(token, _lifecycleToken);
		try {
			return await _raft.TryAddDatabaseNodeAsync(node, tokenSource.Token);
		} catch (NotLeaderException e) {
			throw new LeadershipRequiredException(e);
		} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, _lifecycleToken)) {
			throw new ObjectDisposedException(e.Message, e);
		} catch (OperationCanceledException e) when (e.CancellationToken == tokenSource.Token) {
			throw new OperationCanceledException(e.Message, e, tokenSource.CancellationOrigin);
		} finally {
			await tokenSource.DisposeAsync();
		}
	}

	public async ValueTask<bool> RemoveDatabaseNodeAsync(string databaseId, EndPoint address, CancellationToken token = default) {
		var tokenSource = _multiplexer.Combine(token, _lifecycleToken);
		try {
			return await _raft.RemoveDatabaseNodeAsync(databaseId, address, tokenSource.Token);
		} catch (NotLeaderException e) {
			throw new LeadershipRequiredException(e);
		} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, _lifecycleToken)) {
			throw new ObjectDisposedException(e.Message, e);
		} catch (OperationCanceledException e) when (e.CancellationToken == tokenSource.Token) {
			throw new OperationCanceledException(e.Message, e, tokenSource.CancellationOrigin);
		} finally {
			await tokenSource.DisposeAsync();
		}
	}

	public async ValueTask<bool> RenewLeaderAppointmentAsync(string databaseId, EndPoint leaderAddress, ulong epoch, Guid instanceId, CancellationToken token = default) {
		var leadershipToken = LeadershipToken;
		var tokenSource = _multiplexer.Combine(leadershipToken, token, _lifecycleToken);
		try {
			// When this node becomes a Raft leader, we need to keep existing DPlane appointments
			// alive. To populate appointments, Raft leader needs some time to read information
			// from the state machine. During that period, renewal call needs to be suspended.
			await _readyToRenew.Task.WaitAsync(tokenSource.Token);
			return RenewLeaderAppointment(databaseId, leaderAddress, epoch, instanceId);
		} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, leadershipToken)) {
			throw new LeadershipRequiredException(e);
		} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, _lifecycleToken)) {
			throw new ObjectDisposedException(e.Message, e);
		} catch (OperationCanceledException e) when (e.CancellationToken == tokenSource.Token) {
			throw new OperationCanceledException(e.Message, e, tokenSource.CancellationOrigin);
		} finally {
			await tokenSource.DisposeAsync();
		}
	}

	public async ValueTask<bool> ResignDatabaseLeaderAsync(string databaseId, ulong? epoch, CancellationToken token = default) {
		bool result;
		var tokenSource = _multiplexer.Combine(token, _lifecycleToken);
		try {
			result = await _raft.ResignLeaderAsync(databaseId, epoch, tokenSource.Token)
			         && _appointmentState.TryGetValue(databaseId, out var appointment)
			         && _appointmentState.TryUpdate(databaseId, appointment with { IsResigned = true }, appointment);
		} catch (NotLeaderException e) {
			throw new LeadershipRequiredException(e);
		} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, _lifecycleToken)) {
			throw new ObjectDisposedException(e.Message, e);
		} catch (OperationCanceledException e) when (e.CancellationToken == tokenSource.Token) {
			throw new OperationCanceledException(e.Message, e, tokenSource.CancellationOrigin);
		} finally {
			await tokenSource.DisposeAsync();
		}

		if (result) {
			_appointmentRoundSignal.Set();
		}

		return result;
	}

	public async IAsyncEnumerable<DatabaseCluster> ListenDatabaseAsync(string databaseId, [EnumeratorCancellation] CancellationToken token = default) {
		var tokenSource = CancellationToken.Combine([token, _lifecycleToken]);
		var enumerator = _state.TrackChangesAsync(databaseId, tokenSource.Token).GetAsyncEnumerator();
		try {
			for (;;) {
				ClusterState snapshot;
				try {
					if (!await enumerator.MoveNextAsync())
						break;

					snapshot = enumerator.Current;
				} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, token) || e.CausedBy(tokenSource, _lifecycleToken)) {
					break;
				} catch (OperationCanceledException e) when (e.CancellationToken == tokenSource.Token) {
					throw new OperationCanceledException(e.Message, e, tokenSource.CancellationOrigin);
				}

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
		} finally {
			await enumerator.DisposeAsync();
			tokenSource.Dispose();
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
		var tokenSource = _multiplexer.Combine(token, _lifecycleToken);
		try {
			for (;; tokenSource.Token.ThrowIfCancellationRequested()) {
				IRaftClusterMember leader = await _raft.WaitForLeaderAsync(InfiniteTimeSpan, tokenSource.Token);
				KontrollerMetadata metadata;

				try {
					for (var refreshMetadata = false;
					     !TryParseMetadata(await leader.GetMetadataAsync(refreshMetadata, tokenSource.Token), out metadata);
					     refreshMetadata = true) ;
				} catch {
					continue;
				}

				return GetApiEndPoint(leader.EndPoint, metadata.ApiPort);
			}
		} catch (OperationCanceledException e) when (e.CausedBy(tokenSource, _lifecycleToken)) {
			throw new ObjectDisposedException(e.Message, e);
		} catch (OperationCanceledException e) when (e.CancellationToken == tokenSource.Token) {
			throw new OperationCanceledException(e.Message, e, tokenSource.CancellationOrigin);
		} finally {
			await tokenSource.DisposeAsync();
		}
	}

	IAsyncEnumerator<EndPoint> IAsyncEnumerable<EndPoint>.GetAsyncEnumerator(CancellationToken token) {
		return Task.WhenEach(_raft.Members.Select(member => GetMemberAddressAsync(member, token)))
			.Select(static task => task.IsCompletedSuccessfully ? task.Result : null)
			.SkipNulls()
			.GetAsyncEnumerator(token);

		static Task<EndPoint?> GetMemberAddressAsync(IRaftClusterMember member, CancellationToken token)
			=> member.Status is ClusterMemberStatus.Available ? GetMemberAddressCoreAsync(member, token) : NoEndPointTask;

		static async Task<EndPoint?> GetMemberAddressCoreAsync(IRaftClusterMember member, CancellationToken token)
			=> TryParseMetadata(await member.GetMetadataAsync(refresh: false, token),
				out var metadata)
				? GetApiEndPoint(member.EndPoint, metadata.ApiPort)
				: null;
	}

	private static bool TryParseMetadata(IReadOnlyDictionary<string, string> metadata, out KontrollerMetadata result) {
		if (metadata.TryGetValue(ApiPortMetadataKey, out var apiPortStringValue)
		    && int.TryParse(apiPortStringValue, out var apiPort)) {
			result = new() { ApiPort = apiPort };
			return true;
		}

		result = default;
		return false;
	}

	private static EndPoint GetApiEndPoint(EndPoint kontrollerNode, int apiPort) {
		return kontrollerNode switch {
			DnsEndPoint dns => new DnsEndPoint(dns.Host, apiPort),
			IPEndPoint ip => new IPEndPoint(ip.Address, apiPort),
			_ => kontrollerNode
		};
	}
}
