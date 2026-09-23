// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public class PartitionProcessor(
	int partitionIndex,
	ChannelReader<PartitionEvent> reader,
	IProjectionStateHandler stateHandler,
	string projectionName,
	bool isBiState,
	bool emitEnabled,
	Action<int, IReadOnlyOutputBuffer> onCheckpointMarker,
	Func<string, ValueTask<string?>> loadPersistedState,
	PartitionStateCache sharedPartitionStates,
	int maxPartitionStateCacheSize) {

	private static readonly ILogger Log = Serilog.Log.ForContext<PartitionProcessor>();

	// the two buffers alternate being active/frozen
	private OutputBuffer _activeBuffer = new();
	private OutputBuffer _frozenBuffer = new();
	private readonly PartitionStateCache _stateCache =
		new(maxPartitionStateCacheSize, name: $"partition-{partitionIndex}", projectionName);
	private string? _sharedState;
	private bool _sharedStateInitialized;

	public async Task Run(CancellationToken ct) {
		Log.Debug("Partition {Index} starting for projection {Name}", partitionIndex, projectionName);

		try {
			await foreach (var pe in reader.ReadAllAsync(ct)) {
				if (pe.IsCheckpointMarker)
					HandleCheckpointMarker();
				else if (pe.IsPartitionDeleted)
					await ProcessPartitionDeleted(pe, ct);
				else
					await ProcessEvent(pe, ct);
			}
		} finally {
			await _stateCache.DisposeAsync();
		}
	}

	// Builds the fault that a throwing state handler surfaces as: projection name,
	// handler type, event position, and the handler's message. Only handler
	// invocations are wrapped - infrastructure failures (state-stream reads, cache
	// writes) propagate raw rather than blaming the user's handler.
	private PartitionProcessingException HandlerFault(TFPos position, Exception inner) {
		var tag = CheckpointTag.FromPosition(0, position.CommitPosition, position.PreparePosition);
		return new(projectionName, stateHandler.GetType(), tag.ToString(), inner);
	}

	/// <summary>
	/// Loads partition state into the state handler from cache, persisted result stream, or initializes fresh.
	/// Returns true if the partition is new (not previously seen in this run or persisted).
	/// </summary>
	private async ValueTask<bool> LoadPartitionState(string partitionKey, TFPos position, CancellationToken ct) {
		if (_stateCache.TryGet(partitionKey, out var cachedState)) {
			// A null cached state means the handler explicitly set state to null (e.g. JS null).
			// Load "null" so the handler gets JS null, not a fresh $init state.
			try {
				stateHandler.Load(cachedState ?? "null");
			} catch (Exception ex) {
				throw HandlerFault(position, ex);
			}
			return false;
		}

		var persistedState = await loadPersistedState(partitionKey);
		if (persistedState is not null) {
			Log.Debug("Loaded persisted state for partition {Partition} in projection {Name}",
				partitionKey, projectionName);
			try {
				stateHandler.Load(persistedState);
			} catch (Exception ex) {
				throw HandlerFault(position, ex);
			}
			await _stateCache.Set(partitionKey, persistedState, ct);
			return false;
		}

		try {
			stateHandler.Initialize();
		} catch (Exception ex) {
			throw HandlerFault(position, ex);
		}
		return true;
	}

	// Loads the shared state into the state handler
	private void LoadSharedState(TFPos position) {
		if (!isBiState) return;

		if (!_sharedStateInitialized) {
			try {
				stateHandler.InitializeShared();
			} catch (Exception ex) {
				throw HandlerFault(position, ex);
			}
			_sharedStateInitialized = true;
		} else if (_sharedState != null) {
			try {
				stateHandler.LoadShared(_sharedState);
			} catch (Exception ex) {
				throw HandlerFault(position, ex);
			}
		}
	}

	private async Task ProcessPartitionDeleted(PartitionEvent pe, CancellationToken ct) {
		var partitionKey = pe.PartitionKey!;

		Log.Debug("Processing partition deleted partition={Partition}", partitionKey);

		await LoadPartitionState(partitionKey, pe.LogPosition, ct);
		LoadSharedState(pe.LogPosition);

		var checkpointTag = CheckpointTag.FromPosition(0, pe.LogPosition.CommitPosition, pe.LogPosition.PreparePosition);

		bool processed;
		string newState;
		try {
			processed = stateHandler.ProcessPartitionDeleted(partitionKey, checkpointTag, out newState);
		} catch (Exception ex) {
			throw HandlerFault(pe.LogPosition, ex);
		}

		if (processed) {
			await _stateCache.Set(partitionKey, newState, ct);
			if (newState != null) {
				var stateStreamName = ProjectionNamesBuilder.MakeStateStreamName(projectionName, partitionKey);
				_activeBuffer.SetPartitionState(partitionKey, stateStreamName, newState, ExpectedVersion.Any);
				await sharedPartitionStates.Set(partitionKey, newState, ct);
			}
		}

		_activeBuffer.LastLogPosition = pe.LogPosition;
	}

	private async Task ProcessEvent(PartitionEvent pe, CancellationToken ct) {
		var projEvent = pe.Event!;
		var partitionKey = pe.PartitionKey!;

		Log.Verbose("Processing event stream={Stream} type={EventType} partition={Partition}",
			projEvent.EventStreamId, projEvent.EventType, partitionKey);

		var isNewPartition = await LoadPartitionState(partitionKey, pe.LogPosition, ct);
		LoadSharedState(pe.LogPosition);

		var checkpointTag = CheckpointTag.FromPosition(0, pe.LogPosition.CommitPosition, pe.LogPosition.PreparePosition);

		if (isNewPartition) {
			EmittedEventEnvelope[] createdEmittedEvents;
			try {
				stateHandler.ProcessPartitionCreated(partitionKey, checkpointTag, projEvent, out createdEmittedEvents);
			} catch (Exception ex) {
				throw HandlerFault(pe.LogPosition, ex);
			}
			if (emitEnabled)
				_activeBuffer.AddEmittedEvents(createdEmittedEvents);
		}

		bool processed;
		string newState;
		string newSharedState;
		EmittedEventEnvelope[] emittedEvents;
		try {
			processed = stateHandler.ProcessEvent(
				partitionKey,
				checkpointTag,
				category: null, // todo: is this an important gap?
				projEvent,
				out newState,
				out newSharedState,
				out emittedEvents);
		} catch (Exception ex) {
			throw HandlerFault(pe.LogPosition, ex);
		}

		if (processed) {
			await _stateCache.Set(partitionKey, newState, ct);
			if (newState is not null) {
				var stateStreamName = ProjectionNamesBuilder.MakeStateStreamName(projectionName, partitionKey);
				_activeBuffer.SetPartitionState(partitionKey, stateStreamName, newState, ExpectedVersion.Any);
				await sharedPartitionStates.Set(partitionKey, newState, ct);
			}

			if (isBiState && newSharedState is not null) {
				_sharedState = newSharedState;
				var sharedStreamName = ProjectionNamesBuilder.MakeStateStreamName(projectionName, "");
				_activeBuffer.SetPartitionState("", sharedStreamName, newSharedState, ExpectedVersion.Any);
			}

			if (emitEnabled && emittedEvents is not null)
				_activeBuffer.AddEmittedEvents(emittedEvents);
		}

		_activeBuffer.LastLogPosition = pe.LogPosition;
	}

	private void HandleCheckpointMarker() {
		Log.Debug("Partition {Index} received checkpoint marker", partitionIndex);

		var bufferToFlush = _activeBuffer;
		_activeBuffer = _frozenBuffer;
		_activeBuffer.Clear();
		_frozenBuffer = bufferToFlush;

		onCheckpointMarker(partitionIndex, bufferToFlush);
	}
}
