// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Services.Processing.Phases;

namespace EventStore.Projections.Core.Services.Processing.WorkItems;

class CompletedWorkItem : CheckpointWorkItemBase {
	private readonly IProjectionPhaseCompleter _projection;

	public CompletedWorkItem(IProjectionPhaseCompleter projection)
		: base() {
		_projection = projection;
	}

	protected override void WriteOutput() {
		_projection.Complete();
		NextStage();
	}
}
