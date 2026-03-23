// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Projections.Core.Services.Processing.Emitting;

namespace KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;

public sealed class EmittedEventEnvelope {
	public readonly EmittedEvent Event;
	public readonly EmittedStreamMetadata StreamMetadata;

	public EmittedEventEnvelope(
		EmittedEvent @event, EmittedStreamMetadata streamMetadata = null) {
		Event = @event;
		StreamMetadata = streamMetadata;
	}
}
