// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;

namespace KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;

public class EmittedDataEvent(
	string streamId,
	Guid eventId,
	string eventType,
	bool isJson,
	string data,
	ExtraMetaData metadata,
	CheckpointTag causedByTag,
	CheckpointTag expectedTag,
	Action<long> onCommitted = null)
	: EmittedEvent(streamId, eventId, eventType, causedByTag, expectedTag, onCommitted) {
	public override string Data { get; } = data;

	public ExtraMetaData Metadata { get; } = metadata;

	public override bool IsJson { get; } = isJson;

	public override bool IsReady() => true;

	public override IEnumerable<KeyValuePair<string, string>> ExtraMetaData() => Metadata?.Metadata;

	public override string ToString() => $"Event Id: {EventId}, Event Type: {EventType}, Data: {Data}";
}
