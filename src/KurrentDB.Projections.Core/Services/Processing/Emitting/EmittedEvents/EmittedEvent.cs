// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;

namespace KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;

public abstract class EmittedEvent {
	public readonly string StreamId;
	public readonly Guid EventId;
	public readonly string EventType;

	protected EmittedEvent(
		string streamId, Guid eventId,
		string eventType, CheckpointTag causedByTag, CheckpointTag expectedTag, Action<long> onCommitted = null) {
		ArgumentNullException.ThrowIfNull(causedByTag);
		StreamId = streamId;
		EventId = eventId;
		EventType = eventType;
		CausedByTag = causedByTag;
		ExpectedTag = expectedTag;
		OnCommitted = onCommitted;
	}

	public abstract string Data { get; }

	public CheckpointTag CausedByTag { get; }

	public CheckpointTag ExpectedTag { get; }

	public Action<long> OnCommitted { get; }

	public Guid CausedBy { get; private set; }

	public string CorrelationId { get; private set; }

	public abstract bool IsJson { get; }

	public abstract bool IsReady();

	public virtual IEnumerable<KeyValuePair<string, string>> ExtraMetaData() => null;

	public void SetCausedBy(Guid causedBy) {
		CausedBy = causedBy;
	}

	public void SetCorrelationId(string correlationId) {
		CorrelationId = correlationId;
	}
}
