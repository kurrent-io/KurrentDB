// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

#nullable enable
using System;

namespace EventStore.Core.Services.PersistentSubscription;

public class PersistentSubscriptionSingleStreamPosition : IPersistentSubscriptionStreamPosition {
	public bool IsSingleStreamPosition => true;
	public long StreamEventNumber { get; }
	public bool IsAllStreamPosition => false;
	public bool IsLivePosition => StreamEventNumber == -1L;
	public (long Commit, long Prepare) TFPosition => throw new InvalidOperationException();
	public PersistentSubscriptionSingleStreamPosition(long eventNumber) {
		StreamEventNumber = eventNumber;
	}
	public bool Equals(IPersistentSubscriptionStreamPosition? other) {
		if (other == null)
			throw new InvalidOperationException();
		if (!(other is PersistentSubscriptionSingleStreamPosition))
			throw new InvalidOperationException();
		return StreamEventNumber.Equals(other.StreamEventNumber);
	}

	public int CompareTo(IPersistentSubscriptionStreamPosition? other) {
		if (other == null)
			throw new InvalidOperationException();
		if (!(other is PersistentSubscriptionSingleStreamPosition))
			throw new InvalidOperationException();
		return StreamEventNumber.CompareTo(other.StreamEventNumber);
	}

	public override string ToString() => StreamEventNumber.ToString();
}
