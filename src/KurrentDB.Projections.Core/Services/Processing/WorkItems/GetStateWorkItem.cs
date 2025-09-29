// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;
using KurrentDB.Projections.Core.Services.Processing.Phases;

namespace KurrentDB.Projections.Core.Services.Processing.WorkItems;

internal class GetStateWorkItem(
	IPublisher publisher,
	Guid correlationId,
	Guid projectionId,
	IProjectionPhaseStateManager projection,
	string partition)
	: GetDataWorkItemBase(publisher, correlationId, projectionId, projection, partition) {
	protected override void Reply(PartitionState state, CheckpointTag checkpointTag) {
		_publisher.Publish(new CoreProjectionStatusMessage.StateReport(
			_correlationId,
			_projectionId,
			_partition,
			state?.State,
			checkpointTag));
	}
}
