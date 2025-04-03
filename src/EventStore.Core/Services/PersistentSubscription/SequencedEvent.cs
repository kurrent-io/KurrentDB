// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Data;

namespace EventStore.Core.Services.PersistentSubscription;

public struct SequencedEvent {
	public readonly long Sequence;
	public readonly ResolvedEvent Event;

	public SequencedEvent(long sequence, ResolvedEvent @event) {
		this.Sequence = sequence;
		this.Event = @event;
	}
}
