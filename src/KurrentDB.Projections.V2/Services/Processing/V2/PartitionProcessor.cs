// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public class PartitionProcessor(
	int partitionIndex,
	ChannelReader<PartitionEvent> reader,
	IProjectionStateHandler stateHandler,
	string projectionName,
	bool isBiState,
	bool emitEnabled,
	Func<ulong, OutputBuffer, Task> onCheckpointMarker,
	Func<string, ValueTask<string?>>? loadPersistedState = null,
	ConcurrentDictionary<string, string>? sharedPartitionStates = null) {
	private static readonly ILogger Log = Serilog.Log.ForContext<PartitionProcessor>();

	private OutputBuffer _activeBuffer = new();
	private OutputBuffer _frozenBuffer = new();
	private readonly Dictionary<string, string?> _stateCache = new();
	private string? _sharedState;
	private bool _sharedStateInitialized;

	public async Task Run(CancellationToken ct) {
		Log.Debug("Partition {Index} starting for projection {Name}", partitionIndex, projectionName);

		// Don't use ct for reading: the processor should drain all pending events
		// (including the final checkpoint marker) before stopping. The read loop
		// signals completion by completing the channel via dispatcher.Complete().
		await foreach (var pe in reader.ReadAllAsync(CancellationToken.None)) {
			if (pe.IsCheckpointMarker) {
				await HandleCheckpointMarker(pe.CheckpointMarkerSequence!.Value);
				continue;
			}

			if (pe.IsPartitionDeleted)
				await ProcessPartitionDeleted(pe);
			else
				await ProcessEvent(pe);
		}
	}

	/// <summary>
	/// Loads partition state from cache, persisted result stream, or initializes fresh.
	/// Returns true if the partition is new (not previously seen in this run or persisted).
	/// </summary>
	private async ValueTask<bool> LoadPartitionState(string partitionKey) {
		if (_stateCache.TryGetValue(partitionKey, out var cachedState)) {
			// A null cached state means the handler explicitly set state to null (e.g. JS null).
			// Load "null" so the handler gets JS null, not a fresh $init state.
			stateHandler.Load(cachedState ?? "null");
			return false;
		}

		// Try loading persisted state from the result stream (recovery after restart).
		if (loadPersistedState is not null) {
			var persistedState = await loadPersistedState(partitionKey);
			if (persistedState is not null) {
				Log.Debug("Loaded persisted state for partition {Partition} in projection {Name}",
					partitionKey, projectionName);
				stateHandler.Load(persistedState);
				_stateCache[partitionKey] = persistedState;
				return false;
			}
		}

		stateHandler.Initialize();
		return true;
	}

	private void LoadSharedState() {
		if (!isBiState) return;

		if (!_sharedStateInitialized) {
			stateHandler.InitializeShared();
			_sharedStateInitialized = true;
		} else if (_sharedState != null) {
			stateHandler.LoadShared(_sharedState);
		}
	}

	private async Task ProcessPartitionDeleted(PartitionEvent pe) {
		var partitionKey = pe.PartitionKey!;

		Log.Debug("Processing partition deleted partition={Partition}", partitionKey);

		await LoadPartitionState(partitionKey);
		LoadSharedState();

		var checkpointTag = CheckpointTag.FromPosition(0, pe.LogPosition.CommitPosition, pe.LogPosition.PreparePosition);

		var processed = stateHandler.ProcessPartitionDeleted(partitionKey, checkpointTag, out var newState);

		if (processed) {
			_stateCache[partitionKey] = newState;
			if (newState != null) {
				var stateStreamName = $"$projections-{projectionName}-{partitionKey}-result";
				_activeBuffer.SetPartitionState(partitionKey, stateStreamName, newState, -2); // ExpectedVersion.Any
				sharedPartitionStates?[partitionKey] = newState;
			}
		}

		_activeBuffer.LastLogPosition = pe.LogPosition;
	}

	private async Task ProcessEvent(PartitionEvent pe) {
		var projEvent = pe.Event!;
		var partitionKey = pe.PartitionKey!;

		Log.Debug("Processing event stream={Stream} type={EventType} partition={Partition}",
			projEvent.EventStreamId, projEvent.EventType, partitionKey);

		var isNewPartition = await LoadPartitionState(partitionKey);
		LoadSharedState();

		var checkpointTag = CheckpointTag.FromPosition(0, pe.LogPosition.CommitPosition, pe.LogPosition.PreparePosition);

		if (isNewPartition) {
			stateHandler.ProcessPartitionCreated(partitionKey, checkpointTag, projEvent, out var createdEmittedEvents);
			if (emitEnabled)
				_activeBuffer.AddEmittedEvents(createdEmittedEvents);
		}

		var processed = stateHandler.ProcessEvent(
			partitionKey,
			checkpointTag,
			category: null,
			projEvent,
			out var newState,
			out var newSharedState,
			out var emittedEvents);

		if (processed) {
			_stateCache[partitionKey] = newState;
			if (newState != null) {
				var stateStreamName = $"$projections-{projectionName}-{partitionKey}-result";
				_activeBuffer.SetPartitionState(partitionKey, stateStreamName, newState, -2); // ExpectedVersion.Any
				sharedPartitionStates?[partitionKey] = newState;
			}
		}

		if (isBiState && newSharedState != null) {
			_sharedState = newSharedState;
			var sharedStreamName = $"$projections-{projectionName}--result";
			_activeBuffer.SetPartitionState("", sharedStreamName, newSharedState, -2);
		}

		if (emitEnabled)
			_activeBuffer.AddEmittedEvents(emittedEvents);
		_activeBuffer.LastLogPosition = pe.LogPosition;
	}

	private async Task HandleCheckpointMarker(ulong sequence) {
		Log.Debug("Partition {Index} received checkpoint marker {Sequence}", partitionIndex, sequence);

		var bufferToFlush = _activeBuffer;
		_activeBuffer = _frozenBuffer;
		_activeBuffer.Clear();
		_frozenBuffer = bufferToFlush;

		await onCheckpointMarker(sequence, bufferToFlush);
	}
}
