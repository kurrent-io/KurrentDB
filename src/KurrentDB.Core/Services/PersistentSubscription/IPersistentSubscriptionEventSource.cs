// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;

namespace KurrentDB.Core.Services.PersistentSubscription;

public enum EventSourceKind {
	Stream,
	All,
	Index
}

public interface IPersistentSubscriptionEventSource {
	EventSourceKind Kind { get; }
	bool FromStream => Kind == EventSourceKind.Stream;
	bool FromAll => Kind == EventSourceKind.All;
	bool FromIndex => Kind == EventSourceKind.Index;
	string EventStreamId { get; }
	string IndexName { get; }
	string ToString();
	IPersistentSubscriptionStreamPosition StreamStartPosition { get; }
	IPersistentSubscriptionStreamPosition GetStreamPositionFor(ResolvedEvent @event);
	IPersistentSubscriptionStreamPosition GetStreamPositionFor(string checkpoint);
	IEventFilter EventFilter { get; }
}
