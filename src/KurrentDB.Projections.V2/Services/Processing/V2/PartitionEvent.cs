// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable

using KurrentDB.Core.Data;

namespace KurrentDB.Projections.Core.Services.Processing.V2;

/// <summary>
/// An event routed to a specific partition, or a checkpoint marker, or a partition deleted notification.
/// The partition key is always computed by the dispatcher on the read loop thread.
/// </summary>
public readonly record struct PartitionEvent {
	public ResolvedEvent? Event { get; private init; }
	public string? PartitionKey { get; private init; }
	public TFPos LogPosition { get; private init; }
	public ulong? CheckpointMarkerSequence { get; private init; }
	public bool IsPartitionDeleted { get; private init; }

	public bool IsCheckpointMarker => CheckpointMarkerSequence.HasValue;

	public static PartitionEvent ForEvent(ResolvedEvent @event, string partitionKey, TFPos logPosition)
		=> new() { Event = @event, PartitionKey = partitionKey, LogPosition = logPosition };

	public static PartitionEvent ForPartitionDeleted(string partitionKey, TFPos logPosition)
		=> new() { PartitionKey = partitionKey, LogPosition = logPosition, IsPartitionDeleted = true };

	public static PartitionEvent ForCheckpointMarker(ulong sequence, TFPos logPosition)
		=> new() { CheckpointMarkerSequence = sequence, LogPosition = logPosition };
}
