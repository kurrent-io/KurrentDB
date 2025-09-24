// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;

namespace KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;

internal sealed class EmittedEventResolutionNeeded(string streamId, long revision, Tuple<CheckpointTag, string, long> topCommitted)
	: IValidatedEmittedEvent {
	public string StreamId { get; } = streamId;
	public long Revision { get; } = revision;
	public Tuple<CheckpointTag, string, long> TopCommitted { get; } = topCommitted;
}
