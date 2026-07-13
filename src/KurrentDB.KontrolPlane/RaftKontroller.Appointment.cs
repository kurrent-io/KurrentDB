// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using DotNext.Diagnostics;
using DotNext.Net.Cluster.Consensus.Raft;

namespace KurrentDB.KontrolPlane;

using StateMachine;
using StateMachine.LogEntries;
using StateMachine.Queries;

partial class RaftKontroller {
	// key is database ID, value is the time when the leadership was updated for the particular database
	private readonly ConcurrentDictionary<string, LeaderAppointment> _appointmentState = new();

	// Spin in the loop and process appointments for every database
	private async ValueTask ProcessAppointmentsAsync(CancellationToken token) {
		var tasks = new List<Task>(17);
		var databases = new HashSet<string>(17);
		var deletedDatabases = new HashSet<string>();
		var timer = new PeriodicTimer(_appointmentExpiration);
		try {
			do {
				var snapshot = await _state.CaptureCurrentStateAsync(token);
				try {
					StartAppointments(snapshot, tasks, databases, token);
					RemoveDeletedDatabases(databases, deletedDatabases);

					var task = Task.WhenAll(tasks);
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
				}
			} while (await timer.WaitForNextTickAsync(token));
		}  finally {
			timer.Dispose();
			_appointmentState.Clear();
		}

		static void LeadershipLost(IEnumerable<Exception> exceptions, CancellationToken token) {
			foreach (var e in exceptions) {
				switch (e) {
					case NotLeaderException nle:
						throw new OperationCanceledException(nle.Message, e, token);
					case OperationCanceledException oce when oce.CancellationToken == token:
						throw new OperationCanceledException(oce.Message, oce, token);
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
		List<Task> tasks,
		HashSet<string> databases,
		CancellationToken token) {
		// Process appointment for every database in parallel
		using (clusterState.RentConnection(out var connection)) {
			// Workaround: it's not possible to enumerate to query results within the same connection.
			// Thus, we need to materialize (ToList) the first query
			var currentDBs = connection.GetDatabasesWithEpoch().ToList();
			foreach (var database in currentDBs) {
				databases.Add(database.Id);

				IReadOnlyList<(EndPoint Address, bool IsReadOnlyReplica, bool IsLeader)> nodes = connection
					.GetDatabaseNodes(database.Id)
					.ToList();

				if (IsAppointmentRequired(database.Id, nodes))
					tasks.Add(AppointLeaderAsync(database.Id, database.Epoch, nodes, token));
			}
		}
	}

	private bool IsAppointmentRequired(string databaseId, IReadOnlyList<(EndPoint Address, bool IsReadOnlyReplica, bool IsLeader)> nodes)
		=> nodes is not []
		   && (!_appointmentState.TryGetValue(databaseId, out var appointment)
		       || appointment.IsExpired(_appointmentExpiration));

	private async Task AppointLeaderAsync(string databaseId, ulong epoch, IReadOnlyList<(EndPoint Address, bool IsReadOnlyReplica, bool IsLeader)> nodes, CancellationToken token) {
		var responses = new Dictionary<EndPoint, ReplicaState>(nodes.Count);

		await foreach (var task in Task.WhenEach(GetReplicaState(DataPlane, nodes, token))) {
			try {
				var pair = await task;
				responses.Add(pair.Key, pair.Value);
			} catch (OperationCanceledException e) when (e.CancellationToken == token) {
				return; // cancellation requested, abort appointment
			} catch (Exception) {
				// member is unavailable, don't add it to a collection of successful responses
			}
		}

		// Appoint leader only if we have a quorum
		var quorum = nodes.Count / 2 + 1;
		if (responses.Count < quorum)
			return;

		// Find the node with the max Epoch
		var maxEpoch = responses.Values.Max(static state => state.Epoch);

		// Find the node with the max offset
		var candidate = responses
			.Where(pair => pair.Value.Epoch == maxEpoch)
			.OrderByDescending(static pair => pair.Value.WriterCheckpoint)
			.ThenByDescending(static pair => pair.Value.ChaserCheckpoint)
			.ThenByDescending(static pair => pair.Value.Priority)
			.First()
			.Key;

		// Appoint the leader. Use empty cancellation token because AppointLeaderAsync throws NotLeaderException
		// if the current node is not a leader anymore
		if (await _raft.AppointLeaderAsync(databaseId, epoch, candidate, CancellationToken.None))
			_appointmentState[databaseId] = new LeaderAppointment(candidate, epoch + 1UL); // appointment increments the Epoch

		static IEnumerable<Task<KeyValuePair<EndPoint, ReplicaState>>> GetReplicaState(
			IDataPlane replicas,
			IEnumerable<(EndPoint Address, bool IsReadOnlyReplica, bool IsLeader)> nodes,
			CancellationToken token)
			=> nodes
				.Where(static node => !node.IsReadOnlyReplica) // r/o replicas cannot contribute to the quorum
				.Select(node => GetReplicaStateAsync(replicas, node.Address, token));

		static async Task<KeyValuePair<EndPoint, ReplicaState>> GetReplicaStateAsync(
			IDataPlane replicas,
			EndPoint address,
			CancellationToken token)
			=> new(address, await replicas.GetReplicaStateAsync(address, token));
	}

	private bool RenewLeaderAppointment(string databaseId, EndPoint leaderAddress, ulong epoch) {
		if (!_appointmentState.TryGetValue(databaseId, out var expectedAppointment) || expectedAppointment.Epoch != epoch)
			return false;

		var newAppointment = new LeaderAppointment(leaderAddress, epoch);
		return _appointmentState.TryUpdate(databaseId, newAppointment, expectedAppointment);
	}

	[StructLayout(LayoutKind.Auto)]
	private readonly record struct LeaderAppointment(EndPoint Address, ulong Epoch, Timestamp UpdatedAt) {
		public LeaderAppointment(EndPoint address, ulong epoch)
			: this(address, epoch, new()) {
		}

		public bool IsExpired(TimeSpan expiration) => UpdatedAt.Elapsed >= expiration;
	}
}
