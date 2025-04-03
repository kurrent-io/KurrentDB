// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using EventStore.Projections.Core.Services.Processing.Partitioning;

namespace EventStore.Projections.Core.Services.Processing;

public class EventProcessedResult {
	private readonly EmittedEventEnvelope[] _emittedEvents;
	private readonly PartitionState _oldState;
	private readonly PartitionState _newState;
	private readonly PartitionState _oldSharedState;
	private readonly PartitionState _newSharedState;
	private readonly string _partition;
	private readonly CheckpointTag _checkpointTag;
	private readonly Guid _causedBy;
	private readonly string _correlationId;
	private readonly bool _isPartitionTombstone;

	public EventProcessedResult(
		string partition, CheckpointTag checkpointTag, PartitionState oldState, PartitionState newState,
		PartitionState oldSharedState, PartitionState newSharedState, EmittedEventEnvelope[] emittedEvents,
		Guid causedBy, string correlationId, bool isPartitionTombstone = false) {
		if (partition == null) throw new ArgumentNullException("partition");
		if (checkpointTag == null) throw new ArgumentNullException("checkpointTag");
		_emittedEvents = emittedEvents;
		_causedBy = causedBy;
		_correlationId = correlationId;
		_isPartitionTombstone = isPartitionTombstone;
		_oldState = oldState;
		_newState = newState;
		_oldSharedState = oldSharedState;
		_newSharedState = newSharedState;
		_partition = partition;
		_checkpointTag = checkpointTag;
	}

	public EmittedEventEnvelope[] EmittedEvents {
		get { return _emittedEvents; }
	}

	public PartitionState OldState {
		get { return _oldState; }
	}

	/// <summary>
	/// null - means no state change
	/// </summary>
	public PartitionState NewState {
		get { return _newState; }
	}

	public PartitionState OldSharedState {
		get { return _oldSharedState; }
	}

	/// <summary>
	/// null - means no state change
	/// </summary>
	public PartitionState NewSharedState {
		get { return _newSharedState; }
	}

	public string Partition {
		get { return _partition; }
	}

	public CheckpointTag CheckpointTag {
		get { return _checkpointTag; }
	}

	public Guid CausedBy {
		get { return _causedBy; }
	}

	public string CorrelationId {
		get { return _correlationId; }
	}

	public bool IsPartitionTombstone {
		get { return _isPartitionTombstone; }
	}
}
