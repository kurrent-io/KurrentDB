// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Core.Bus;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Phases;
using KurrentDB.Projections.Core.Services.Processing.WorkItems;
using static KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection.checkpoint_manager;

public class FakeCoreProjection
	: ICoreProjection,
		ICoreProjectionForProcessingPhase,
		IHandle<EventReaderSubscriptionMessage.ReaderAssignedReader> {
	public readonly List<CheckpointCompleted> _checkpointCompletedMessages = [];
	public readonly List<CheckpointLoaded> _checkpointLoadedMessages = [];
	public readonly List<PrerecordedEventsLoaded> _prerecordedEventsLoadedMessages = [];
	private readonly List<Failed> _failedMessages = [];

	public void Handle(CheckpointCompleted message) {
		_checkpointCompletedMessages.Add(message);
	}

	public void Handle(CheckpointLoaded message) {
		_checkpointLoadedMessages.Add(message);
	}

	public void Handle(RestartRequested message) {
		throw new NotImplementedException();
	}

	public void Handle(Failed message) {
		_failedMessages.Add(message);
	}

	public void Handle(PrerecordedEventsLoaded message) {
		_prerecordedEventsLoadedMessages.Add(message);
	}

	public void CompletePhase() {
		CompletePhaseInvoked++;
	}

	public void SetFaulted(string reason) {
		throw new NotImplementedException();
	}

	public void SetFaulted(Exception ex) {
		throw new NotImplementedException();
	}

	public void SetFaulting(string reason) {
		throw new NotImplementedException();
	}

	public void SetCurrentCheckpointSuggestedWorkItem(CheckpointSuggestedWorkItem checkpointSuggestedWorkItem) {
		throw new NotImplementedException();
	}

	public void EnsureTickPending() {
		throw new NotImplementedException();
	}

	public CheckpointTag LastProcessedEventPosition => throw new NotImplementedException();

	public int SubscribedInvoked { get; set; }

	public int CompletePhaseInvoked { get; set; }

	public void Subscribed() {
		SubscribedInvoked++;
	}

	public void Handle(EventReaderSubscriptionMessage.ReaderAssignedReader message) {
		throw new NotImplementedException();
	}
}
