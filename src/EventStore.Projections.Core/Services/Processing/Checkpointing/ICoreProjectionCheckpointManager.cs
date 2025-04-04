// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing.Partitioning;

namespace EventStore.Projections.Core.Services.Processing.Checkpointing;

public interface ICoreProjectionCheckpointManager {
	void Initialize();
	void Start(CheckpointTag checkpointTag, PartitionState rootPartitionState);
	void Stopping();
	void Stopped();
	void GetStatistics(ProjectionStatistics info);


	void StateUpdated(string partition, PartitionState oldState, PartitionState newState);
	void PartitionCompleted(string partition);
	void EventProcessed(CheckpointTag checkpointTag, float progress);

	/// <summary>
	/// Suggests a checkpoint which may complete immediately or be delayed
	/// </summary>
	/// <param name="checkpointTag"></param>
	/// <param name="progress"></param>
	/// <returns>true - if checkpoint has been completed (or skipped)</returns>
	bool CheckpointSuggested(CheckpointTag checkpointTag, float progress);

	void Progress(float progress);

	void BeginLoadPrerecordedEvents(CheckpointTag checkpointTag);

	void BeginLoadPartitionStateAt(
		string statePartition, CheckpointTag requestedStateCheckpointTag,
		Action<PartitionState> loadCompleted);

	void RecordEventOrder(ResolvedEvent resolvedEvent, CheckpointTag orderCheckpointTag, Action committed);
	CheckpointTag LastProcessedEventPosition { get; }
}
