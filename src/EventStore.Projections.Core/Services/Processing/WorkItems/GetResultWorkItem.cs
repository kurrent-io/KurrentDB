// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Partitioning;
using EventStore.Projections.Core.Services.Processing.Phases;

namespace EventStore.Projections.Core.Services.Processing.WorkItems;

class GetResultWorkItem : GetDataWorkItemBase {
	public GetResultWorkItem(
		IPublisher publisher,
		Guid correlationId,
		Guid projectionId,
		IProjectionPhaseStateManager projection,
		string partition)
		: base(publisher, correlationId, projectionId, projection, partition) {
	}

	protected override void Reply(PartitionState state, CheckpointTag checkpointTag) {
		if (state == null)
			_publisher.Publish(
				new CoreProjectionStatusMessage.ResultReport(
					_correlationId,
					_projectionId,
					_partition,
					null,
					checkpointTag));
		else
			_publisher.Publish(
				new CoreProjectionStatusMessage.ResultReport(
					_correlationId,
					_projectionId,
					_partition,
					state.Result,
					checkpointTag));
	}
}
