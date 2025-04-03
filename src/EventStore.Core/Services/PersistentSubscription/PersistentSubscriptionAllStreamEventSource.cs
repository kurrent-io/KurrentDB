// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;

namespace EventStore.Core.Services.PersistentSubscription;

public class PersistentSubscriptionAllStreamEventSource : IPersistentSubscriptionEventSource {
	public bool FromStream => false;
	public string EventStreamId => throw new InvalidOperationException();
	public bool FromAll => true;
	public override string ToString() => SystemStreams.AllStream;
	public IEventFilter EventFilter { get; }

	public PersistentSubscriptionAllStreamEventSource(IEventFilter eventFilter) {
		EventFilter = eventFilter;
	}

	public PersistentSubscriptionAllStreamEventSource() {
		EventFilter = null;
	}

	public IPersistentSubscriptionStreamPosition StreamStartPosition  => new PersistentSubscriptionAllStreamPosition(0L, 0L);
	public IPersistentSubscriptionStreamPosition GetStreamPositionFor(ResolvedEvent @event) {
		if (@event.OriginalPosition.HasValue) {
			return new PersistentSubscriptionAllStreamPosition(@event.OriginalPosition.Value.CommitPosition, @event.OriginalPosition.Value.PreparePosition);
		}
		throw new InvalidOperationException();
	}
	public IPersistentSubscriptionStreamPosition GetStreamPositionFor(string checkpoint) {
		const string C = "C:";
		const string P = "P:";
		string[] tokens = checkpoint.Split("/");
		Debug.Assert(tokens.Length == 2);
		Debug.Assert(tokens[0].StartsWith(C));
		Debug.Assert(tokens[1].StartsWith(P));
		long commitPosition = long.Parse(tokens[0].Substring(C.Length));
		long preparePosition = long.Parse(tokens[1].Substring(P.Length));
		return new PersistentSubscriptionAllStreamPosition(commitPosition, preparePosition);
	}
}
