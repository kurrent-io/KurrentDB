// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Projections.Core.Services.Processing.Checkpointing;

namespace EventStore.Projections.Core.Services.Processing;

public sealed class TaggedResolvedEvent {
	public readonly ResolvedEvent ResolvedEvent;
	public readonly CheckpointTag ReaderPosition;

	public TaggedResolvedEvent(ResolvedEvent resolvedEvent, CheckpointTag readerPosition) {
		ResolvedEvent = resolvedEvent;
		ReaderPosition = readerPosition;
	}
}
