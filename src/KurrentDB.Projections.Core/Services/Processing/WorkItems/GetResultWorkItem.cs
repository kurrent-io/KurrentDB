// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;
using KurrentDB.Projections.Core.Services.Processing.Phases;

namespace KurrentDB.Projections.Core.Services.Processing.WorkItems;

internal class GetResultWorkItem(
	IPublisher publisher,
	Guid correlationId,
	Guid projectionId,
	IProjectionPhaseStateManager projection,
	string partition)
	: GetDataWorkItemBase(publisher, correlationId, projectionId, projection, partition) {
	private readonly string _partition = partition;

	protected override void Reply(PartitionState state, CheckpointTag checkpointTag) {
		Publisher.Publish(
			new CoreProjectionStatusMessage.ResultReport(CorrelationId, ProjectionId, _partition, state?.Result, checkpointTag)
		);
	}
}
