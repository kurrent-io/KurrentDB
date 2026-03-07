// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using KurrentDB.Core.Data;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

/// <summary>
/// An event routed to a specific partition, or a checkpoint marker.
/// </summary>
public readonly record struct PartitionEvent {
	public ResolvedEvent? Event { get; init; }
	public string? PartitionKey { get; init; }
	public TFPos LogPosition { get; init; }
	public ulong? CheckpointMarkerSequence { get; init; }

	public bool IsCheckpointMarker => CheckpointMarkerSequence.HasValue;

	/// <summary>True if the partition key needs to be computed by the processor.</summary>
	public bool NeedsPartitionKeyComputation => Event is not null && PartitionKey is null;

	public static PartitionEvent ForEvent(ResolvedEvent @event, string partitionKey, TFPos logPosition)
		=> new() { Event = @event, PartitionKey = partitionKey, LogPosition = logPosition };

	public static PartitionEvent ForDeferredEvent(ResolvedEvent @event, TFPos logPosition)
		=> new() { Event = @event, LogPosition = logPosition };

	public static PartitionEvent ForCheckpointMarker(ulong sequence, TFPos logPosition)
		=> new() { CheckpointMarkerSequence = sequence, LogPosition = logPosition };
}
