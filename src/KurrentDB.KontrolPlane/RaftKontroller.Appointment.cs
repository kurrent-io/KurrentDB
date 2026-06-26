// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using DotNext.Diagnostics;

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
				StartAppointments(snapshot, tasks, databases, token);
				RemoveDeletedDatabases(databases, deletedDatabases);

				await Task.WhenAll(tasks);
				tasks.Clear();
				deletedDatabases.Clear();
				databases.Clear();
			} while (await timer.WaitForNextTickAsync(token));
		} finally {
			timer.Dispose();
			_appointmentState.Clear();
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
		Snapshot snapshot,
		List<Task> tasks,
		HashSet<string> databases,
		CancellationToken token) {
		// Process appointment for every database in parallel
		using (snapshot.RentConnection(out var connection)) {
			foreach (var databaseId in connection.GetDatabases()) {
				databases.Add(databaseId);

				IReadOnlyList<(EndPoint Address, bool IsReadOnlyReplica, bool IsLeader)> nodes = connection
					.GetDatabaseNodes(databaseId)
					.ToList();

				if (IsAppointmentRequired(databaseId, nodes))
					tasks.Add(AppointLeaderAsync(databaseId, nodes, token));
			}
		}
	}

	private bool IsAppointmentRequired(string databaseId, IReadOnlyList<(EndPoint Address, bool IsReadOnlyReplica, bool IsLeader)> nodes)
		=> nodes is not []
		   && (!_appointmentState.TryGetValue(databaseId, out var appointment)
		       || appointment.IsExpired(_appointmentExpiration));

	private async Task AppointLeaderAsync(string databaseId, IReadOnlyList<(EndPoint Address, bool IsReadOnlyReplica, bool IsLeader)> nodes, CancellationToken token) {
		var responses = new Dictionary<EndPoint, ReplicaState>(nodes.Count);

		await foreach (var task in Task.WhenEach(GetReplicaState(ReplicaSet, nodes, token))) {
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
			.MaxBy(static pair => pair.Value.UncommittedOffset)
			.Key;

		// Appoint the leader
		await _raft.AppointLeaderAsync(databaseId, candidate, token);
		_appointmentState[databaseId] = new LeaderAppointment(candidate);

		static IEnumerable<Task<KeyValuePair<EndPoint, ReplicaState>>> GetReplicaState(
			IDatabaseReplicaSet replicas,
			IEnumerable<(EndPoint Address, bool IsReadOnlyReplica, bool IsLeader)> nodes,
			CancellationToken token)
			=> nodes
				.Where(static node => !node.IsReadOnlyReplica) // r/o replicas cannot contribute to the qorum
				.Select(node => GetReplicaStateAsync(replicas, node.Address, token));

		static async Task<KeyValuePair<EndPoint, ReplicaState>> GetReplicaStateAsync(
			IDatabaseReplicaSet replicas,
			EndPoint address,
			CancellationToken token)
			=> new(address, await replicas.GetReplicaStateAsync(address, token));
	}

	private bool RenewLeaderAppointment(string databaseId, EndPoint leaderAddress) {
		if (!_appointmentState.TryGetValue(databaseId, out var expectedAppointment))
			return false;

		var newAppointment = new LeaderAppointment(leaderAddress);
		return _appointmentState.TryUpdate(databaseId, newAppointment, expectedAppointment);
	}

	[StructLayout(LayoutKind.Auto)]
	private readonly record struct LeaderAppointment(EndPoint Address, Timestamp UpdatedAt) {
		public LeaderAppointment(EndPoint address)
			: this(address, new()) {
		}

		public bool IsExpired(TimeSpan expiration) => UpdatedAt.Elapsed >= expiration;
	}
}
