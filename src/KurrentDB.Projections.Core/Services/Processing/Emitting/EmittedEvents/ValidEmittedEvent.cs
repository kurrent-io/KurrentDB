// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;

internal sealed class ValidEmittedEvent(long revision) : IValidatedEmittedEvent {
	public long Revision { get; } = revision;
}
