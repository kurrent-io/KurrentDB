// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Projections.Core.Services.Processing.Checkpointing;

namespace EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;

public abstract class EmittedEvent {
	public readonly string StreamId;
	public readonly Guid EventId;
	public readonly string EventType;
	private readonly CheckpointTag _causedByTag;
	private readonly CheckpointTag _expectedTag;
	private readonly Action<long> _onCommitted;
	private Guid _causedBy;
	private string _correlationId;

	protected EmittedEvent(
		string streamId, Guid eventId,
		string eventType, CheckpointTag causedByTag, CheckpointTag expectedTag, Action<long> onCommitted = null) {
		if (causedByTag == null) throw new ArgumentNullException("causedByTag");
		StreamId = streamId;
		EventId = eventId;
		EventType = eventType;
		_causedByTag = causedByTag;
		_expectedTag = expectedTag;
		_onCommitted = onCommitted;
	}

	public abstract string Data { get; }

	public CheckpointTag CausedByTag {
		get { return _causedByTag; }
	}

	public CheckpointTag ExpectedTag {
		get { return _expectedTag; }
	}

	public Action<long> OnCommitted {
		get { return _onCommitted; }
	}

	public Guid CausedBy {
		get { return _causedBy; }
	}

	public string CorrelationId {
		get { return _correlationId; }
	}

	public abstract bool IsJson { get; }

	public abstract bool IsReady();

	public virtual IEnumerable<KeyValuePair<string, string>> ExtraMetaData() {
		return null;
	}

	public void SetCausedBy(Guid causedBy) {
		_causedBy = causedBy;
	}

	public void SetCorrelationId(string correlationId) {
		_correlationId = correlationId;
	}
}
