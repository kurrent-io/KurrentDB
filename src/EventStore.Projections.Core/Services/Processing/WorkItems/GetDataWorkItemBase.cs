// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Partitioning;
using EventStore.Projections.Core.Services.Processing.Phases;

namespace EventStore.Projections.Core.Services.Processing.WorkItems;

abstract class GetDataWorkItemBase : WorkItem {
	protected readonly IPublisher _publisher;
	protected readonly string _partition;
	protected Guid _correlationId;
	protected Guid _projectionId;
	private readonly IProjectionPhaseStateManager _projection;
	private PartitionState _state;
	private CheckpointTag _lastProcessedCheckpointTag;

	protected GetDataWorkItemBase(
		IPublisher publisher,
		Guid correlationId,
		Guid projectionId,
		IProjectionPhaseStateManager projection,
		string partition)
		: base(null) {
		if (partition == null) throw new ArgumentNullException("partition");
		_publisher = publisher;
		_partition = partition;
		_correlationId = correlationId;
		_projectionId = projectionId;
		_projection = projection;
	}

	protected override void GetStatePartition() {
		NextStage(_partition);
	}

	protected override void Load(CheckpointTag checkpointTag) {
		_lastProcessedCheckpointTag = _projection.LastProcessedEventPosition;
		_projection.BeginGetPartitionStateAt(
			_partition,
			_lastProcessedCheckpointTag,
			LoadCompleted,
			lockLoaded: false);
	}

	private void LoadCompleted(PartitionState state) {
		_state = state;
		NextStage();
	}

	protected override void WriteOutput() {
		Reply(_state, _lastProcessedCheckpointTag);
		NextStage();
	}

	protected abstract void Reply(PartitionState state, CheckpointTag checkpointTag);
}
