// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Partitioning;
using EventStore.Projections.Core.Services.Processing.WorkItems;

namespace EventStore.Projections.Core.Services.Processing.Phases;

public interface IProjectionPhaseCompleter {
	void Complete();
}

public interface IProjectionPhaseCheckpointManager {
	void NewCheckpointStarted(CheckpointTag checkpointTag);
	void SetCurrentCheckpointSuggestedWorkItem(CheckpointSuggestedWorkItem checkpointSuggestedWorkItem);
}

public interface IProjectionPhaseStateManager {
	void BeginGetPartitionStateAt(
		string statePartition, CheckpointTag at, Action<PartitionState> loadCompleted,
		bool lockLoaded);

	void UnlockAndForgetBefore(CheckpointTag checkpointTag);

	CheckpointTag LastProcessedEventPosition { get; }
}

public interface IEventProcessingProjectionPhase : IProjectionPhaseStateManager {
	EventProcessedResult ProcessCommittedEvent(EventReaderSubscriptionMessage.CommittedEventReceived message,
		string partition);

	void FinalizeEventProcessing(
		EventProcessedResult result, CheckpointTag eventCheckpointTag, float progress);

	void RecordEventOrder(ResolvedEvent resolvedEvent, CheckpointTag orderCheckpointTag, Action completed);

	void EmitEofResult(
		string partition, string resultBody, CheckpointTag causedBy, Guid causedByGuid, string correlationId);

	EventProcessedResult ProcessPartitionDeleted(string partition, CheckpointTag deletedPosition);
}
