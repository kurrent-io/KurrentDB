// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using Serilog;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

public class PartitionProcessor {
	private static readonly ILogger Log = Serilog.Log.ForContext<PartitionProcessor>();

	private readonly int _partitionIndex;
	private readonly ChannelReader<PartitionEvent> _reader;
	private readonly IProjectionStateHandler _stateHandler;
	private readonly string _projectionName;
	private readonly Func<ulong, OutputBuffer, Task> _onCheckpointMarker;

	private OutputBuffer _activeBuffer = new();
	private OutputBuffer _frozenBuffer = new();
	private readonly Dictionary<string, string> _stateCache = new();

	public PartitionProcessor(
		int partitionIndex,
		ChannelReader<PartitionEvent> reader,
		IProjectionStateHandler stateHandler,
		string projectionName,
		Func<ulong, OutputBuffer, Task> onCheckpointMarker) {
		_partitionIndex = partitionIndex;
		_reader = reader;
		_stateHandler = stateHandler;
		_projectionName = projectionName;
		_onCheckpointMarker = onCheckpointMarker;
	}

	public async Task Run(CancellationToken ct) {
		Log.Debug("Partition {Index} starting for projection {Name}", _partitionIndex, _projectionName);

		await foreach (var pe in _reader.ReadAllAsync(ct)) {
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

		if (!_stateCache.TryGetValue(partitionKey, out var currentState))
			currentState = "{}";

		_stateHandler.Load(currentState);

		var checkpointTag = CheckpointTag.FromPosition(0, pe.LogPosition.CommitPosition, pe.LogPosition.PreparePosition);

		var processed = _stateHandler.ProcessEvent(
			partitionKey,
			checkpointTag,
			category: null,
			projEvent,
			out var newState,
			out var newSharedState,
			out var emittedEvents);

		if (processed && newState != null) {
			_stateCache[partitionKey] = newState;
			var stateStreamName = $"$projections-{_projectionName}-{partitionKey}-result";
			_activeBuffer.SetPartitionState(partitionKey, stateStreamName, newState, -2); // ExpectedVersion.Any
		}

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
