// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;
using KurrentDB.Projections.Core.Services.Processing.WorkItems;

namespace KurrentDB.Projections.Core.Services.Processing.Phases;

public interface IProjectionPhaseCompleter {
	void Complete();
}

public interface IProjectionPhaseCheckpointManager {
	void NewCheckpointStarted(CheckpointTag checkpointTag);
	void SetCurrentCheckpointSuggestedWorkItem(CheckpointSuggestedWorkItem checkpointSuggestedWorkItem);
}

public interface IProjectionPhaseStateManager {
	void BeginGetPartitionStateAt(string statePartition, CheckpointTag at, Action<PartitionState> loadCompleted, bool lockLoaded);

	CheckpointTag LastProcessedEventPosition { get; }
}

public interface IEventProcessingProjectionPhase : IProjectionPhaseStateManager {
	EventProcessedResult ProcessCommittedEvent(EventReaderSubscriptionMessage.CommittedEventReceived message,string partition);

	void FinalizeEventProcessing(EventProcessedResult result, CheckpointTag eventCheckpointTag, float progress);

	void RecordEventOrder(ResolvedEvent resolvedEvent, CheckpointTag orderCheckpointTag, Action completed);

	EventProcessedResult ProcessPartitionDeleted(string partition, CheckpointTag deletedPosition);
}
