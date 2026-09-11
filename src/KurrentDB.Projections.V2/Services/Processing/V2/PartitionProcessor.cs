// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
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

	// Wraps a state-handler invocation so a throw faults the projection with a
	// debuggable fault reason: projection name, handler type, event position, and
	// the handler's message. Only handler invocations are wrapped - infrastructure
	// failures (state-stream reads, cache writes) propagate raw rather than
	// blaming the user's handler.
	private T InvokeHandler<T>(TFPos position, Func<T> invoke) {
		try {
			return invoke();
		} catch (Exception ex) {
			var tag = CheckpointTag.FromPosition(0, position.CommitPosition, position.PreparePosition);
			throw new PartitionProcessingException(projectionName, stateHandler.GetType(), tag.ToString(), ex);
		}
	}

	private void InvokeHandler(TFPos position, Action invoke) =>
		InvokeHandler<object?>(position, () => {
			invoke();
			return null;
		});

	/// <summary>
	/// Loads partition state into the state handler from cache, persisted result stream, or initializes fresh.
	/// Returns true if the partition is new (not previously seen in this run or persisted).
	/// </summary>
	private async ValueTask<bool> LoadPartitionState(string partitionKey, TFPos position, CancellationToken ct) {
		if (_stateCache.TryGet(partitionKey, out var cachedState)) {
			// A null cached state means the handler explicitly set state to null (e.g. JS null).
			// Load "null" so the handler gets JS null, not a fresh $init state.
			InvokeHandler(position, () => stateHandler.Load(cachedState ?? "null"));
			return false;
		}

		var persistedState = await loadPersistedState(partitionKey);
		if (persistedState is not null) {
			Log.Debug("Loaded persisted state for partition {Partition} in projection {Name}",
				partitionKey, projectionName);
			InvokeHandler(position, () => stateHandler.Load(persistedState));
			await _stateCache.Set(partitionKey, persistedState, ct);
			return false;
		}

		InvokeHandler(position, stateHandler.Initialize);
		return true;
	}

	// Loads the shared state into the state handler
	private void LoadSharedState(TFPos position) {
		if (!isBiState) return;

		if (!_sharedStateInitialized) {
			InvokeHandler(position, stateHandler.InitializeShared);
			_sharedStateInitialized = true;
		} else if (_sharedState != null) {
			InvokeHandler(position, () => stateHandler.LoadShared(_sharedState));
		}
	}

	private async Task ProcessPartitionDeleted(PartitionEvent pe, CancellationToken ct) {
		var partitionKey = pe.PartitionKey!;

		Log.Debug("Processing partition deleted partition={Partition}", partitionKey);

		await LoadPartitionState(partitionKey, pe.LogPosition, ct);
		LoadSharedState(pe.LogPosition);

		var checkpointTag = CheckpointTag.FromPosition(0, pe.LogPosition.CommitPosition, pe.LogPosition.PreparePosition);

		var (processed, newState) = InvokeHandler(pe.LogPosition, () => {
			var p = stateHandler.ProcessPartitionDeleted(partitionKey, checkpointTag, out var state);
			return (p, state);
		});

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
			var createdEmittedEvents = InvokeHandler(pe.LogPosition, () => {
				stateHandler.ProcessPartitionCreated(partitionKey, checkpointTag, projEvent, out var created);
				return created;
			});
			if (emitEnabled)
				_activeBuffer.AddEmittedEvents(createdEmittedEvents);
		}

		var (processed, newState, newSharedState, emittedEvents) = InvokeHandler(pe.LogPosition, () => {
			var p = stateHandler.ProcessEvent(
				partitionKey,
				checkpointTag,
				category: null, // todo: is this an important gap?
				projEvent,
				out var state,
				out var sharedState,
				out var emitted);
			return (p, state, sharedState, emitted);
		});

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
