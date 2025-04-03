// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable
using System;
using EventStore.Core.Data;

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
		if (other == null) throw new InvalidOperationException();
		if (!(other is PersistentSubscriptionSingleStreamPosition)) throw new InvalidOperationException();
		return StreamEventNumber.Equals(other.StreamEventNumber);
	}

	public int CompareTo(IPersistentSubscriptionStreamPosition? other) {
		if (other == null) throw new InvalidOperationException();
		if (!(other is PersistentSubscriptionSingleStreamPosition)) throw new InvalidOperationException();
		return StreamEventNumber.CompareTo(other.StreamEventNumber);
	}

	public override string ToString() => StreamEventNumber.ToString();
}
