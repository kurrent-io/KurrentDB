// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Phases;

namespace EventStore.Projections.Core.Services.Processing.WorkItems;

class ProgressWorkItem : CheckpointWorkItemBase {
	private readonly ICoreProjectionCheckpointManager _checkpointManager;
	private readonly IProgressResultWriter _resultWriter;
	private readonly float _progress;

	public ProgressWorkItem(ICoreProjectionCheckpointManager checkpointManager, IProgressResultWriter resultWriter,
		float progress)
		: base(null) {
		_checkpointManager = checkpointManager;
		_resultWriter = resultWriter;
		_progress = progress;
	}

	protected override void WriteOutput() {
		_checkpointManager.Progress(_progress);
		_resultWriter.WriteProgress(_progress);
		NextStage();
	}
}
