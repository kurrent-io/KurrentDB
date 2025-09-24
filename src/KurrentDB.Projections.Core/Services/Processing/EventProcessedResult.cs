// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;

namespace KurrentDB.Projections.Core.Services.Processing;

public class EventProcessedResult {
	public EventProcessedResult(
		string partition, CheckpointTag checkpointTag, PartitionState oldState, PartitionState newState,
		PartitionState oldSharedState, PartitionState newSharedState, EmittedEventEnvelope[] emittedEvents,
		Guid causedBy, string correlationId) {
		ArgumentNullException.ThrowIfNull(partition);
		ArgumentNullException.ThrowIfNull(checkpointTag);
		EmittedEvents = emittedEvents;
		CausedBy = causedBy;
		CorrelationId = correlationId;
		OldState = oldState;
		NewState = newState;
		OldSharedState = oldSharedState;
		NewSharedState = newSharedState;
		Partition = partition;
		CheckpointTag = checkpointTag;
	}

	public EmittedEventEnvelope[] EmittedEvents { get; }

	public PartitionState OldState { get; }

	/// <summary>
	/// null - means no state change
	/// </summary>
	public PartitionState NewState { get; }

	public PartitionState OldSharedState { get; }

	/// <summary>
	/// null - means no state change
	/// </summary>
	public PartitionState NewSharedState { get; }

	public string Partition { get; }

	public CheckpointTag CheckpointTag { get; }

	public Guid CausedBy { get; }

	public string CorrelationId { get; }
}
