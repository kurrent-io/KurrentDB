// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Bus;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Phases;
using KurrentDB.Projections.Core.Services.Processing.WorkItems;

namespace EventStore.Projections.Core.Tests.Services.core_projection.checkpoint_manager;

public class FakeCoreProjection : ICoreProjection,
	ICoreProjectionForProcessingPhase,
	IHandle<EventReaderSubscriptionMessage.ReaderAssignedReader> {
	public readonly List<KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.CheckpointCompleted> _checkpointCompletedMessages =
		new List<KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.CheckpointCompleted>();

	public readonly List<KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.CheckpointLoaded> _checkpointLoadedMessages =
		new List<KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.CheckpointLoaded>();

	public readonly List<KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.PrerecordedEventsLoaded> _prerecordedEventsLoadedMessages =
		new List<KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.PrerecordedEventsLoaded>();

	public readonly List<KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.Failed> _failedMessages =
		new List<KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.Failed>();

	public void Handle(KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.CheckpointCompleted message) {
		_checkpointCompletedMessages.Add(message);
	}

	public void Handle(KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.CheckpointLoaded message) {
		_checkpointLoadedMessages.Add(message);
	}

	public void Handle(KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.RestartRequested message) {
		throw new System.NotImplementedException();
	}

	public void Handle(KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.Failed message) {
		_failedMessages.Add(message);
	}

	public void Handle(KurrentDB.Projections.Core.Messages.CoreProjectionProcessingMessage.PrerecordedEventsLoaded message) {
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
