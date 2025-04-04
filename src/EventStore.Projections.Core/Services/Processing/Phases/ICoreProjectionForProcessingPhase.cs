// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.WorkItems;

namespace EventStore.Projections.Core.Services.Processing.Phases;

public interface ICoreProjectionForProcessingPhase {
	void CompletePhase();
	void SetFaulted(string reason);
	void SetFaulted(Exception ex);
	void SetFaulting(string reason);
	void SetCurrentCheckpointSuggestedWorkItem(CheckpointSuggestedWorkItem checkpointSuggestedWorkItem);
	void EnsureTickPending();
	CheckpointTag LastProcessedEventPosition { get; }
	void Subscribed();
}
