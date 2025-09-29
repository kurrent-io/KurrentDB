// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Phases;

namespace KurrentDB.Projections.Core.Services.Processing.WorkItems;

public class CheckpointSuggestedWorkItem(
	IProjectionPhaseCheckpointManager projectionPhase,
	EventReaderSubscriptionMessage.CheckpointSuggested message,
	ICoreProjectionCheckpointManager checkpointManager)
	: CheckpointWorkItemBase {
	private bool _completed;
	private bool _completeRequested;

	protected override void WriteOutput() {
		projectionPhase.SetCurrentCheckpointSuggestedWorkItem(this);
		if (checkpointManager.CheckpointSuggested(message.CheckpointTag, message.Progress)) {
			projectionPhase.SetCurrentCheckpointSuggestedWorkItem(null);
			_completed = true;
		}

		projectionPhase.NewCheckpointStarted(message.CheckpointTag);
		NextStage();
	}

	protected override void CompleteItem() {
		if (_completed)
			NextStage();
		else
			_completeRequested = true;
	}

	internal void CheckpointCompleted() {
		if (_completeRequested)
			NextStage();
		else
			_completed = true;
	}
}
