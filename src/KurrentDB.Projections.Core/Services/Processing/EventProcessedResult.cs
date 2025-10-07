// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Common.Utils;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using KurrentDB.Projections.Core.Services.Processing.Partitioning;

namespace KurrentDB.Projections.Core.Services.Processing;

public class EventProcessedResult(
	string partition,
	CheckpointTag checkpointTag,
	PartitionState oldState,
	PartitionState newState,
	PartitionState oldSharedState,
	PartitionState newSharedState,
	EmittedEventEnvelope[] emittedEvents,
	Guid causedBy,
	string correlationId) {
	public EmittedEventEnvelope[] EmittedEvents { get; } = emittedEvents;

	public PartitionState OldState { get; } = oldState;

	/// <summary>
	/// null - means no state change
	/// </summary>
	public PartitionState NewState { get; } = newState;

	public PartitionState OldSharedState { get; } = oldSharedState;

	/// <summary>
	/// null - means no state change
	/// </summary>
	public PartitionState NewSharedState { get; } = newSharedState;

	public string Partition { get; } = Ensure.NotNull(partition);

	public CheckpointTag CheckpointTag { get; } = Ensure.NotNull(checkpointTag);

	public Guid CausedBy { get; } = causedBy;

	public string CorrelationId { get; } = correlationId;
}
