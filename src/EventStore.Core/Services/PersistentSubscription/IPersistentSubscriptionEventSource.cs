// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
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
