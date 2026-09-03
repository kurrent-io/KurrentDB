// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotNext;
using DotNext.Diagnostics;
using DotNext.Net.Cluster.Consensus.Raft;
using DotNext.Threading;

namespace KurrentDB.KontrolPlane.Raft;

using DataPlane;
using StateMachine;
using StateMachine.LogEntries;
using StateMachine.Queries;

partial class RaftKontroller {
	// key is database ID, value is the time when the leadership was updated for the particular database
	private readonly ConcurrentDictionary<string, LeaderAppointment> _appointmentState = new();
	private readonly ConcurrentDictionary<string, ulong> _reusableEpochs = new();
	private readonly TimeSpan _heartbeatTimeout;
	private readonly AsyncAutoResetEvent _appointmentRoundSignal = new(initialState: false);

	// Spin in the loop and process appointments for every database
	private async ValueTask ProcessAppointmentsAsync(CancellationToken token) {
		var tasks = new List<Task>(17);
		var databases = new HashSet<string>(17);
		var deletedDatabases = new HashSet<string>();
		var activeMembers = new HashSet<EndPoint>();
		var dataPlane = DataPlaneClientFactory.Invoke();
		try {
			for (var pauseDuration = _heartbeatTimeout;; await _appointmentRoundSignal.WaitAsync(pauseDuration, token)) {
				var snapshot = await _state.CaptureCurrentStateAsync(token);
				try {
					StartAppointments(snapshot, dataPlane, tasks, databases, activeMembers, token);

					var task = Task.WhenAll(tasks);

					// parallel actions
					RemoveDeletedDatabases(databases, deletedDatabases);
					await dataPlane.ReclaimConnectionsAsync(activeMembers, token);

					// wait for all communications to be finished
					await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing
					                          | ConfigureAwaitOptions.ContinueOnCapturedContext);

					// If one or many appointment processes throws NotLeaderException, it means
					// that the current node lost its leadership in the cluster
					if (task.Exception?.InnerExceptions is { } exceptions) {
						LeadershipLost(exceptions, token);
					}
				} finally {
					snapshot.Release();
					tasks.Clear();
					deletedDatabases.Clear();
					databases.Clear();
					activeMembers.Clear();
				}
			}
		} finally {
			_appointmentState.Clear();
			_reusableEpochs.Clear();
			await dataPlane.DisposeAsync();
		}

		static void LeadershipLost(IEnumerable<Exception> exceptions, CancellationToken token) {
			foreach (var e in exceptions) {
				switch (e) {
					case NotLeaderException:
					case OperationCanceledException oce when oce.CancellationToken == token:
						throw new OperationCanceledException(e.Message, e, token);
				}
			}
		}
	}

	private void RemoveDeletedDatabases(
		IReadOnlySet<string> existingDatabases,
		HashSet<string> deletedDatabases) {
		// Remove deleted databases from the appointment state
		foreach (var databaseId in _appointmentState.Keys) {
			if (!existingDatabases.Contains(databaseId))
				deletedDatabases.Add(databaseId);
		}

		foreach (var databaseId in deletedDatabases) {
			_appointmentState.TryRemove(databaseId, out _);
		}
	}

	private void StartAppointments(
		ClusterState clusterState,
		IDataPlane dataPlane,
		List<Task> tasks,
		HashSet<string> databases,
		HashSet<EndPoint> activeMembers,
		CancellationToken token) {
		// Process appointment for every database in parallel
		using (clusterState.RentConnection(out var connection)) {
			// Workaround: it's not possible to enumerate to query results within the same connection.
			// Thus, we need to materialize (ToList) the first query
			var currentDBs = connection.GetDatabasesWithEpoch().ToList();
			foreach (var database in currentDBs) {
				databases.Add(database.Id);

				var nodes = connection
					.GetDatabaseNodes(database.Id)
					.Select(static node => (node.Address, node.Role))
					.ToList();

				ImportMembers(CollectionsMarshal.AsSpan(nodes), activeMembers);
				if (IsAppointmentRequired(database.Id, nodes, out var resignedLeader))
					tasks.Add(AppointLeaderAsync(database.Id, database.Epoch, dataPlane, nodes, resignedLeader, token));
			}
		}

		static void ImportMembers(ReadOnlySpan<(EndPoint Address, DatabaseNodeRole Role)> input, HashSet<EndPoint> output) {
			foreach (ref readonly var node in input) {
				output.Add(node.Address);
			}
		}
	}

	private bool IsAppointmentRequired(string databaseId,
		IReadOnlyList<(EndPoint Address, DatabaseNodeRole Role)> nodes,
		out EndPoint? resignedLeader) {
		if (nodes is [] || !_appointmentState.TryGetValue(databaseId, out var appointment)) {
			resignedLeader = null;
			return true;
		}

		if (appointment.IsResigned) {
			resignedLeader = appointment.Address;
			return true;
		}

		resignedLeader = null;
		return appointment.IsExpired(_heartbeatTimeout);
	}

	private async Task AppointLeaderAsync(
		string databaseId,
		ulong currentEpoch,
		IDataPlane dataPlane,
		IReadOnlyList<(EndPoint Address, DatabaseNodeRole Role)> nodes,
		EndPoint? resignedLeader,
		CancellationToken token) {
		_logger.Information($"Appointing leader for database '{databaseId}'");
		(var responses, var maxEpoch, currentEpoch)
			= await FenceDatabaseAsync(databaseId, dataPlane, nodes, currentEpoch, token);

		// Find the node with the max offset
		(EndPoint? Address, Guid InstanceId) candidate = responses
			.Where(pair => pair.Value.Epoch == maxEpoch)
			.OrderByDescending(static pair => pair.Value.WriterCheckpoint)
			.ThenByDescending(static pair => pair.Value.ChaserCheckpoint)
			.ThenByDescending(
				resignedLeader is null ? Func<KeyValuePair<EndPoint, ReplicaState>, int>.Constant(0) : resignedLeader.GetOrder)
			.ThenByDescending(static pair => pair.Value.Priority)
			.Select(static pair => (pair.Key, pair.Value.InstanceId))
			.FirstOrDefault();

		// Appoint the leader. Use empty cancellation token because AppointLeaderAsync throws NotLeaderException
		// if the current node is not a leader anymore
		if (candidate.Address is not null && await _raft.AppointLeaderAsync(databaseId, currentEpoch, candidate.Address, candidate.InstanceId, CancellationToken.None)) {
			_logger.Information($"DPlane node '{candidate.Address}' with instance id '{candidate.InstanceId}' is appointed as leader for database '{databaseId}'");
			_appointmentState[databaseId] = new(candidate.Address, currentEpoch, candidate.InstanceId);
			_state.NotifyDatabaseChanged(databaseId);
		}
	}

	private async Task<(IReadOnlyDictionary<EndPoint, ReplicaState> State, ulong MaxEpoch, ulong Epoch)> FenceDatabaseAsync(
		string databaseId,
		IDataPlane dataPlane,
		IReadOnlyList<(EndPoint Address, DatabaseNodeRole Role)> nodes,
		ulong currentEpoch,
		CancellationToken token) {
		// bump epoch
		var responses = new Dictionary<EndPoint, ReplicaState>(nodes.Count);
		ulong maxEpoch = 0UL, newEpoch = currentEpoch + 1UL;

		// When KPlane is started on top of existing database, we need to get the response
		// for reach node, not for quorum only, to find the max observable epoch, because
		// the epoch in KPlane is 0
		for (var requiresAllNodes = databaseId is Database.MainDatabaseId && currentEpoch is 0UL;
		     nodes.Count > 0 && await BumpEpochAsync(databaseId, currentEpoch, ref newEpoch, token);
		     responses.Clear()) {
			int quorum;
			using (var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(token)) {
				await foreach (var task in FenceDatabaseAsync(dataPlane, nodes, newEpoch, out quorum, requiresAllNodes,
					               tokenSource.Token)) {
					try {
						var pair = await task;
						responses.Add(pair.Key, pair.Value);

						if (responses.Count >= quorum) {
							// Don't break the loop, we want to make sure
							// that all background tasks related to the network access are finished
							await tokenSource.CancelAsync();
						}
					} catch (Exception) when (token.IsCancellationRequested) {
						responses.Clear();
						goto exit; // cancellation requested, abort appointment
					} catch (Exception) {
						// member is unavailable, don't add it to a collection of successful responses
					}
				}
			}

			// Appoint leader only if we have a quorum
			if (responses.Count < quorum) {
				// Epoch is increased, but the quorum can't see it, we can reuse epoch in the next round
				_reusableEpochs[databaseId] = newEpoch;
				responses.Clear();
				break;
			}

			// Find the node with the max Epoch
			maxEpoch = responses.Values.Max(static state => state.Epoch);

			// when running KPlane on top of existing database, the epoch within the database
			// can be larger than epoch in KPlane.
			if (maxEpoch < newEpoch)
				break;

			currentEpoch = newEpoch;
			newEpoch = maxEpoch + 1UL;
		}

		exit:
		return (responses, maxEpoch, newEpoch);
	}

	private ValueTask<bool> BumpEpochAsync(string databaseId, ulong currentEpoch, ref ulong newEpoch, CancellationToken token) {
		ValueTask<bool> task;

		if (_reusableEpochs.TryRemove(databaseId, out var cachedEpoch)) {
			newEpoch = cachedEpoch;
			task = ValueTask.FromResult(true);
		} else {
			task = _raft.BumpEpochAsync(databaseId, currentEpoch, newEpoch, token);
		}

		return task;
	}

	private IAsyncEnumerable<Task<KeyValuePair<EndPoint, ReplicaState>>> FenceDatabaseAsync(
		IDataPlane dataPlane,
		IReadOnlyList<(EndPoint Address, DatabaseNodeRole Role)> nodes,
		ulong newEpoch,
		out int quorum,
		bool requiresAllNodes,
		CancellationToken token) {
		var regularNodes = new List<Task<KeyValuePair<EndPoint, ReplicaState>>>(nodes.Count);
		regularNodes.AddRange(nodes
			.Where(static node => node.Role is DatabaseNodeRole.Regular) // r/o replicas cannot contribute to the quorum
			.Select(static node => node.Address)
			.Select(address => FenceDatabaseNodeAsync(dataPlane, address, newEpoch, token)));

		quorum = requiresAllNodes
			? int.Max(regularNodes.Count, _mainDatabaseClusterSize)
			: regularNodes.Count / 2 + 1;

		return Task.WhenEach(regularNodes);

		static async Task<KeyValuePair<EndPoint, ReplicaState>> FenceDatabaseNodeAsync(IDataPlane dataPlane,
			EndPoint address,
			ulong currentEpoch,
			CancellationToken token)
			=> new(address, await dataPlane.FenceAsync(address, currentEpoch, token));
	}

	private bool RenewLeaderAppointment(string databaseId, EndPoint leaderAddress, ulong epoch, Guid instanceId) {
		if (!_appointmentState.TryGetValue(databaseId, out var expectedAppointment)
		    || expectedAppointment.Epoch != epoch
		    || !expectedAppointment.Address.Equals(leaderAddress)
		    || expectedAppointment.InstanceId != instanceId
		    || expectedAppointment.IsResigned)
			return false;

		var newAppointment = expectedAppointment with {
			Epoch = epoch,
			RenewedAt = new(),
		};
		return _appointmentState.TryUpdate(databaseId, newAppointment, expectedAppointment);
	}

	[StructLayout(LayoutKind.Auto)]
	private readonly record struct LeaderAppointment(EndPoint Address, ulong Epoch, Timestamp RenewedAt, Guid InstanceId) {
		public LeaderAppointment(EndPoint address, ulong epoch, Guid instanceId)
			: this(address, epoch, new(), instanceId) {
		}

		public bool IsResigned { get; init; }

		public bool IsExpired(TimeSpan expiration) => RenewedAt.Elapsed >= expiration;
	}
}

file static class EndPointExtensions {
	public static int GetOrder(this EndPoint resignedLeader, KeyValuePair<EndPoint, ReplicaState> candidate)
		=> Unsafe.BitCast<bool, byte>(!resignedLeader.Equals(candidate.Key));
}
