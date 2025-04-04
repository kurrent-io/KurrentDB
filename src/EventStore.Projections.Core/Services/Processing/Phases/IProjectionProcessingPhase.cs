// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;

namespace EventStore.Projections.Core.Services.Processing.Phases;

public interface IProjectionProcessingPhase : IDisposable,
	IHandle<CoreProjectionManagementMessage.GetState>,
	IHandle<CoreProjectionManagementMessage.GetResult>,
	IHandle<CoreProjectionProcessingMessage.PrerecordedEventsLoaded> {
	CheckpointTag AdjustTag(CheckpointTag tag);

	void InitializeFromCheckpoint(CheckpointTag checkpointTag);

	//TODO: remove from - it is passed for validation purpose only
	void Subscribe(CheckpointTag from, bool fromCheckpoint);

	void ProcessEvent();

	void EnsureUnsubscribed();

	void SetProjectionState(PhaseState state);

	void GetStatistics(ProjectionStatistics info);

	CheckpointTag MakeZeroCheckpointTag();
	ICoreProjectionCheckpointManager CheckpointManager { get; }
	IEmittedStreamsTracker EmittedStreamsTracker { get; }
}
