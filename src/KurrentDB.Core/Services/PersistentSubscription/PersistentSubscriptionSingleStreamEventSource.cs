// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;

namespace KurrentDB.Core.Services.PersistentSubscription;

public sealed class PersistentSubscriptionSingleStreamEventSource : IPersistentSubscriptionEventSource {
	public EventSourceKind Kind => EventSourceKind.Stream;
	public string EventStreamId { get; }
	public string IndexName => throw new InvalidOperationException();
	public IEventFilter EventFilter => null;

	public PersistentSubscriptionSingleStreamEventSource(string eventStreamId) {
		EventStreamId = eventStreamId ?? throw new ArgumentNullException(nameof(eventStreamId));
	}
	public override string ToString() => EventStreamId;
	public IPersistentSubscriptionStreamPosition StreamStartPosition => new PersistentSubscriptionSingleStreamPosition(0L);
	public IPersistentSubscriptionStreamPosition GetStreamPositionFor(ResolvedEvent @event) => new PersistentSubscriptionSingleStreamPosition(@event.OriginalEventNumber);
	public IPersistentSubscriptionStreamPosition GetStreamPositionFor(string checkpoint) => new PersistentSubscriptionSingleStreamPosition(long.Parse(checkpoint));
}
