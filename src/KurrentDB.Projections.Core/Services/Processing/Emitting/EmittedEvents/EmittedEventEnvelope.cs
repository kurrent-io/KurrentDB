// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;

public sealed class EmittedEventEnvelope(EmittedEvent @event, EmittedStream.WriterConfiguration.StreamMetadata streamMetadata = null) {
	public readonly EmittedEvent Event = @event;
	public readonly EmittedStream.WriterConfiguration.StreamMetadata StreamMetadata = streamMetadata;
}
