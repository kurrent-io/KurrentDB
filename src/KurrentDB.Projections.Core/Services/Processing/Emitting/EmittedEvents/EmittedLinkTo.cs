// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;

namespace KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;

public class EmittedLinkTo(
	string streamId,
	Guid eventId,
	string targetStreamId,
	CheckpointTag causedByTag,
	CheckpointTag expectedTag,
	Action<long> onCommitted = null)
	: EmittedEvent(streamId, eventId, "$>", causedByTag, expectedTag, onCommitted) {
	private long? _eventNumber;

	public override string Data => !IsReady()
		? throw new InvalidOperationException("Link target has not been yet committed")
		: $"{_eventNumber.Value.ToString(CultureInfo.InvariantCulture)}@{targetStreamId}";

	public override bool IsJson => false;

	[MemberNotNullWhen(true, nameof(_eventNumber))]
	public override bool IsReady() => _eventNumber != null;

	public void SetTargetEventNumber(long eventNumber) {
		if (_eventNumber != null)
			throw new InvalidOperationException("Target event number has been already set");
		_eventNumber = eventNumber;
	}
}
