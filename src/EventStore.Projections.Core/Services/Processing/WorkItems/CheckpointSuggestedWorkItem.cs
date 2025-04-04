// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Phases;

namespace EventStore.Projections.Core.Services.Processing.WorkItems;

public class CheckpointSuggestedWorkItem : CheckpointWorkItemBase {
	private readonly IProjectionPhaseCheckpointManager _projectionPhase;
	private readonly EventReaderSubscriptionMessage.CheckpointSuggested _message;
	private readonly ICoreProjectionCheckpointManager _checkpointManager;

	private bool _completed = false;
	private bool _completeRequested = false;

	public CheckpointSuggestedWorkItem(
		IProjectionPhaseCheckpointManager projectionPhase,
		EventReaderSubscriptionMessage.CheckpointSuggested message,
		ICoreProjectionCheckpointManager checkpointManager)
		: base() {
		_projectionPhase = projectionPhase;
		_message = message;
		_checkpointManager = checkpointManager;
	}

	protected override void WriteOutput() {
		_projectionPhase.SetCurrentCheckpointSuggestedWorkItem(this);
		if (_checkpointManager.CheckpointSuggested(_message.CheckpointTag, _message.Progress)) {
			_projectionPhase.SetCurrentCheckpointSuggestedWorkItem(null);
			_completed = true;
		}

		_projectionPhase.NewCheckpointStarted(_message.CheckpointTag);
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
