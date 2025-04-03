// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing.Checkpointing;

namespace EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;

sealed class EmittedEventResolutionNeeded : IValidatedEmittedEvent {
	public string StreamId { get; }
	public long Revision { get; }
	public Tuple<CheckpointTag, string, long> TopCommitted { get; }

	public EmittedEventResolutionNeeded(string streamId, long revision, Tuple<CheckpointTag, string, long> topCommitted) {
		StreamId = streamId;
		Revision = revision;
		TopCommitted = topCommitted;
	}
}
