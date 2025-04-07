// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;

namespace EventStore.Core.Services.PersistentSubscription;

public interface IPersistentSubscriptionEventSource {
	bool FromStream { get; }
	string EventStreamId { get; }
	bool FromAll { get; }
	string ToString();
	IPersistentSubscriptionStreamPosition StreamStartPosition { get; }
	IPersistentSubscriptionStreamPosition GetStreamPositionFor(ResolvedEvent @event);
	IPersistentSubscriptionStreamPosition GetStreamPositionFor(string checkpoint);
	IEventFilter EventFilter { get; }
}
