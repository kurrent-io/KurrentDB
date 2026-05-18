// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Services.Processing.Phases;

namespace KurrentDB.Projections.Core.Services.Processing.WorkItems;

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
