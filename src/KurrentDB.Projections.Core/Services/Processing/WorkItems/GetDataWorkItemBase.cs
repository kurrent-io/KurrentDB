// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;
using KurrentDB.Core.Bus;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;
using KurrentDB.Projections.Core.Services.Processing.Phases;

namespace KurrentDB.Projections.Core.Services.Processing.WorkItems;

internal abstract class GetDataWorkItemBase(
	IPublisher publisher,
	Guid correlationId,
	Guid projectionId,
	IProjectionPhaseStateManager projection,
	string partition)
	: WorkItem(null) {
	protected readonly IPublisher Publisher = publisher;
	protected readonly string Partition = Ensure.NotNull(partition);
	protected Guid CorrelationId = correlationId;
	protected Guid ProjectionId = projectionId;
	private PartitionState _state;
	private CheckpointTag _lastProcessedCheckpointTag;

	protected override void GetStatePartition() {
		NextStage(Partition);
	}

	protected override void Load() {
		_lastProcessedCheckpointTag = projection.LastProcessedEventPosition;
		projection.BeginGetPartitionStateAt(Partition, _lastProcessedCheckpointTag, LoadCompleted, lockLoaded: false);
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
