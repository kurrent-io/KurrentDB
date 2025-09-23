// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace KurrentDB.Projections.Core.Services.Processing.Partitioning;

public class PartitionStateUpdateManager {
	private class State {
		public PartitionState PartitionState;
		public CheckpointTag ExpectedTag;
	}

	private readonly Dictionary<string, State> _states = new();
	private readonly ProjectionNamesBuilder _namingBuilder;
	private readonly EmittedStream.WriterConfiguration.StreamMetadata _partitionCheckpointStreamMetadata = new(maxCount: 2);

	public PartitionStateUpdateManager(ProjectionNamesBuilder namingBuilder) {
		ArgumentNullException.ThrowIfNull(namingBuilder);
		_namingBuilder = namingBuilder;
	}

	public void StateUpdated(string partition, PartitionState state, CheckpointTag basedOn) {
		if (_states.TryGetValue(partition, out var stateEntry)) {
			stateEntry.PartitionState = state;
		} else {
			_states.Add(partition, new State { PartitionState = state, ExpectedTag = basedOn });
		}
	}

	public void EmitEvents(IEventWriter eventWriter) {
		if (_states.Count > 0) {
			var list = new List<EmittedEventEnvelope>();
			foreach (var (partition, state) in _states) {
				var streamId = _namingBuilder.MakePartitionCheckpointStreamName(partition);
				var data = state.PartitionState.Serialize();
				var causedBy = state.PartitionState.CausedBy;
				var expectedTag = state.ExpectedTag;
				list.Add(new(
					new EmittedDataEvent(
						streamId, Guid.NewGuid(), ProjectionEventTypes.PartitionCheckpoint, true,
						data, null, causedBy, expectedTag), _partitionCheckpointStreamMetadata));
			}

			//NOTE: order yb is required to satisfy internal emit events validation
			// which ensures that events are ordered by causedBy tag.
			// it is too strong check, but ...
			eventWriter.ValidateOrderAndEmitEvents(list.OrderBy(v => v.Event.CausedByTag).ToArray());
		}
	}

	public void PartitionCompleted(string partition) {
		_states.Remove(partition);
	}
}
