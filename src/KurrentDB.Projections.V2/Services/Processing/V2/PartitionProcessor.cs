// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public class PartitionProcessor {
	private static readonly ILogger Log = Serilog.Log.ForContext<PartitionProcessor>();

	private readonly int _partitionIndex;
	private readonly ChannelReader<PartitionEvent> _reader;
	private readonly IProjectionStateHandler _stateHandler;
	private readonly string _projectionName;
	private readonly bool _isBiState;
	private readonly bool _emitEnabled;
	private readonly Func<ulong, OutputBuffer, Task> _onCheckpointMarker;

	private OutputBuffer _activeBuffer = new();
	private OutputBuffer _frozenBuffer = new();
	private readonly Dictionary<string, string?> _stateCache = new();
	private string? _sharedState;
	private bool _sharedStateInitialized;

	public PartitionProcessor(
		int partitionIndex,
		ChannelReader<PartitionEvent> reader,
		IProjectionStateHandler stateHandler,
		string projectionName,
		bool isBiState,
		bool emitEnabled,
		Func<ulong, OutputBuffer, Task> onCheckpointMarker) {
		_partitionIndex = partitionIndex;
		_reader = reader;
		_stateHandler = stateHandler;
		_projectionName = projectionName;
		_isBiState = isBiState;
		_emitEnabled = emitEnabled;
		_onCheckpointMarker = onCheckpointMarker;
	}

	public async Task Run(CancellationToken ct) {
		Log.Debug("Partition {Index} starting for projection {Name}", _partitionIndex, _projectionName);

		// Don't use ct for reading: the processor should drain all pending events
		// (including the final checkpoint marker) before stopping. The read loop
		// signals completion by completing the channel via dispatcher.Complete().
		await foreach (var pe in _reader.ReadAllAsync(CancellationToken.None)) {
			if (pe.IsCheckpointMarker) {
				await HandleCheckpointMarker(pe.CheckpointMarkerSequence!.Value);
				continue;
			}

			ProcessEvent(pe);
		}
	}

	private void ProcessEvent(PartitionEvent pe) {
		var projEvent = pe.Event!;
		var partitionKey = pe.PartitionKey!;

		Log.Debug("Processing event stream={Stream} type={EventType} partition={Partition}",
			projEvent.EventStreamId, projEvent.EventType, partitionKey);

		var isNewPartition = !_stateCache.ContainsKey(partitionKey);

		if (!isNewPartition)
			_stateHandler.Load(_stateCache[partitionKey]);
		else
			_stateHandler.Initialize();

		if (_isBiState) {
			if (!_sharedStateInitialized) {
				_stateHandler.InitializeShared();
				_sharedStateInitialized = true;
			} else if (_sharedState != null) {
				_stateHandler.LoadShared(_sharedState);
			}
		}

		var checkpointTag = CheckpointTag.FromPosition(0, pe.LogPosition.CommitPosition, pe.LogPosition.PreparePosition);

		if (isNewPartition) {
			_stateHandler.ProcessPartitionCreated(partitionKey, checkpointTag, projEvent, out var createdEmittedEvents);
			if (_emitEnabled)
				_activeBuffer.AddEmittedEvents(createdEmittedEvents);
		}

		var processed = _stateHandler.ProcessEvent(
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
				var stateStreamName = $"$projections-{_projectionName}-{partitionKey}-result";
				_activeBuffer.SetPartitionState(partitionKey, stateStreamName, newState, -2); // ExpectedVersion.Any
			}
		}

		if (_isBiState && newSharedState != null) {
			_sharedState = newSharedState;
			var sharedStreamName = $"$projections-{_projectionName}--result";
			_activeBuffer.SetPartitionState("", sharedStreamName, newSharedState, -2);
		}

		if (_emitEnabled)
			_activeBuffer.AddEmittedEvents(emittedEvents);
		_activeBuffer.LastLogPosition = pe.LogPosition;
	}

	private async Task HandleCheckpointMarker(ulong sequence) {
		Log.Debug("Partition {Index} received checkpoint marker {Sequence}", _partitionIndex, sequence);

		var bufferToFlush = _activeBuffer;
		_activeBuffer = _frozenBuffer;
		_activeBuffer.Clear();
		_frozenBuffer = bufferToFlush;

		await _onCheckpointMarker(sequence, bufferToFlush);
	}
}
