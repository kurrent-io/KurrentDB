// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Phases;
using EventStore.Projections.Core.Services.Processing.WorkItems;

namespace EventStore.Projections.Core.Tests.Services.core_projection.checkpoint_manager;

public class FakeCoreProjection : ICoreProjection,
	ICoreProjectionForProcessingPhase,
	IHandle<EventReaderSubscriptionMessage.ReaderAssignedReader> {
	public readonly List<CoreProjectionProcessingMessage.CheckpointCompleted> _checkpointCompletedMessages =
		new List<CoreProjectionProcessingMessage.CheckpointCompleted>();

	public readonly List<CoreProjectionProcessingMessage.CheckpointLoaded> _checkpointLoadedMessages =
		new List<CoreProjectionProcessingMessage.CheckpointLoaded>();

	public readonly List<CoreProjectionProcessingMessage.PrerecordedEventsLoaded> _prerecordedEventsLoadedMessages =
		new List<CoreProjectionProcessingMessage.PrerecordedEventsLoaded>();

	public readonly List<CoreProjectionProcessingMessage.Failed> _failedMessages =
		new List<CoreProjectionProcessingMessage.Failed>();

	public void Handle(CoreProjectionProcessingMessage.CheckpointCompleted message) {
		_checkpointCompletedMessages.Add(message);
	}

	public void Handle(CoreProjectionProcessingMessage.CheckpointLoaded message) {
		_checkpointLoadedMessages.Add(message);
	}

	public void Handle(CoreProjectionProcessingMessage.RestartRequested message) {
		throw new System.NotImplementedException();
	}

	public void Handle(CoreProjectionProcessingMessage.Failed message) {
		_failedMessages.Add(message);
	}

	public void Handle(CoreProjectionProcessingMessage.PrerecordedEventsLoaded message) {
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

	public CheckpointTag LastProcessedEventPosition {
		get { throw new NotImplementedException(); }
	}

	public int SubscribedInvoked { get; set; }
	public int CompletePhaseInvoked { get; set; }

	public void Subscribed() {
		SubscribedInvoked++;
	}

	public void Handle(EventReaderSubscriptionMessage.ReaderAssignedReader message) {
		throw new NotImplementedException();
	}
}
