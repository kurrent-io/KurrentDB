// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotNext.Collections.Generic;
using DotNext.IO;
using DotNext.Net.Cluster.Consensus.Raft.StateMachine;
using DotNext.Threading;

namespace KurrentDB.KontrolPlane.Raft.StateMachine;

partial class ClusterStateMachine {
	// Apply log entry from the WAL to the current state
	private async ValueTask<long> ApplyAsync(LogEntry entry, CancellationToken token) {
		var currentState = _state;
		var commandInfo = new CommandInfo(entry);
		switch (entry.CommandId) {
			case LogEntries.AddOrUpdateDatabase.TypeId:
				Apply(currentState,
					await DeserializeAsync<LogEntries.AddOrUpdateDatabase>(entry, token),
					in commandInfo);
				break;
			case LogEntries.RemoveDatabase.TypeId:
				Apply(currentState,
					await DeserializeAsync<LogEntries.RemoveDatabase>(entry, token),
					in commandInfo,
					entry.Context as StrongBox<bool>);
				break;
			case LogEntries.AddOrUpdateDatabaseNode.TypeId:
				Apply(currentState,
					await DeserializeAsync<LogEntries.AddOrUpdateDatabaseNode>(entry, token),
					in commandInfo);
				break;
			case LogEntries.RemoveDatabaseNode.TypeId:
				Apply(currentState,
					await DeserializeAsync<LogEntries.RemoveDatabaseNode>(entry, token),
					in commandInfo,
					entry.Context as StrongBox<bool>);
				break;
			case LogEntries.AppointLeader.TypeId:
				Apply(currentState,
					await DeserializeAsync<LogEntries.AppointLeader>(entry, token),
					in commandInfo,
					entry.Context as StrongBox<bool>);
				break;
			case LogEntries.AddOrIgnoreDatabaseNode.TypeId:
				Apply(currentState,
					await DeserializeAsync<LogEntries.AddOrIgnoreDatabaseNode>(entry, token),
					in commandInfo,
					entry.Context as StrongBox<bool>);
				break;
			default:
				Debug.Fail($"Unexpected entry type {entry.CommandId}");
				break;
		}

		// Produce snapshot in the background if needed
		if (entry.Index % SnapshotDepth is 0 && _snapshotTask is null or { IsCompleted: true }) {
			currentState.ReclaimGarbage();
			_snapshotTask = SaveSnapshotAsync(currentState, currentState.LastAppliedCommand, token);
		}

		return entry.Index;
	}

	private void Apply(ClusterState currentState, LogEntries.AddOrUpdateDatabase command, in CommandInfo commandInfo) {
		currentState.Update(command, in commandInfo);

		_databases.GetOrAdd(command.DatabaseId, static _ => new AsyncStateTracker()).TryAdvance();
	}

	private void Apply(ClusterState currentState,
		LogEntries.RemoveDatabase command,
		in CommandInfo commandInfo,
		StrongBox<bool>? resultContainer) {
		var result = currentState.Update(command, in commandInfo);

		_databases.TryRemove(command.DatabaseId).ValueOrDefault?.TryComplete();

		resultContainer?.Value = result;
	}

	private void Apply(ClusterState currentState, LogEntries.AddOrUpdateDatabaseNode command, in CommandInfo commandInfo) {
		currentState.Update(command, in commandInfo);

		_databases.TryGetValue(command.DatabaseId).ValueOrDefault?.TryAdvance();
	}

	private void Apply(ClusterState currentState,
		LogEntries.RemoveDatabaseNode command,
		in CommandInfo commandInfo,
		StrongBox<bool>? resultContainer) {
		var result = currentState.Update(command, in commandInfo);

		_databases.TryGetValue(command.DatabaseId).ValueOrDefault?.TryAdvance();

		resultContainer?.Value = result;
	}

	private void Apply(ClusterState currentState, LogEntries.AppointLeader command, in CommandInfo commandInfo, StrongBox<bool>? resultContainer) {
		var result = currentState.Update(command, in commandInfo);

		_databases.TryGetValue(command.DatabaseId).ValueOrDefault?.TryAdvance();
		resultContainer?.Value = result;
	}

	private void Apply(ClusterState currentState,
		LogEntries.AddOrIgnoreDatabaseNode command,
		in CommandInfo commandInfo,
		StrongBox<bool>? resultContainer) {
		var result = currentState.Update(command, in commandInfo);

		if (result) {
			_databases.TryGetValue(command.DatabaseId).ValueOrDefault?.TryAdvance();
		}

		resultContainer?.Value = result;
	}

	private static ValueTask<T> DeserializeAsync<T>(in LogEntry entry, CancellationToken token)
		where T : class, LogEntries.IProtobufSerializable<T> {
		ValueTask<T> task;
		if (entry.TryGetPayload(out var sequence)) {
			// fast path, deserialize from memory
			try {
				task = new(T.Parser.ParseFrom(sequence));
			} catch (Exception e) {
				task = ValueTask.FromException<T>(e);
			}
		} else {
			task = DeserializeSlowAsync(entry, token);
		}

		return task;

		static async ValueTask<T> DeserializeSlowAsync(LogEntry entry, CancellationToken token) {
			using var owner = await entry.ToMemoryAsync(token: token);
			return T.Parser.ParseFrom(owner.Memory.Span);
		}
	}
}
