// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;

namespace EventStore.Projections.Core.Services.Processing.Partitioning;

public class PartitionStateUpdateManager {
	private class State {
		public PartitionState PartitionState;
		public CheckpointTag ExpectedTag;
	}

	private readonly Dictionary<string, State> _states = new Dictionary<string, State>();
	private readonly ProjectionNamesBuilder _namingBuilder;

	private readonly EmittedStream.WriterConfiguration.StreamMetadata _partitionCheckpointStreamMetadata =
		new EmittedStream.WriterConfiguration.StreamMetadata(maxCount: 2);

	public PartitionStateUpdateManager(ProjectionNamesBuilder namingBuilder) {
		if (namingBuilder == null) throw new ArgumentNullException("namingBuilder");
		_namingBuilder = namingBuilder;
	}

	public void StateUpdated(string partition, PartitionState state, CheckpointTag basedOn) {
		State stateEntry;
		if (_states.TryGetValue(partition, out stateEntry)) {
			stateEntry.PartitionState = state;
		} else {
			_states.Add(partition, new State {PartitionState = state, ExpectedTag = basedOn});
		}
	}

	public void EmitEvents(IEventWriter eventWriter) {
		if (_states.Count > 0) {
			var list = new List<EmittedEventEnvelope>();
			foreach (var entry in _states) {
				var partition = entry.Key;
				var streamId = _namingBuilder.MakePartitionCheckpointStreamName(partition);
				var data = entry.Value.PartitionState.Serialize();
				var causedBy = entry.Value.PartitionState.CausedBy;
				var expectedTag = entry.Value.ExpectedTag;
				list.Add(
					new EmittedEventEnvelope(
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
