// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable
using System;

namespace EventStore.Core.Services.PersistentSubscription;

public class PersistentSubscriptionAllStreamPosition : IPersistentSubscriptionStreamPosition {
	public bool IsSingleStreamPosition => false;
	public long StreamEventNumber => throw new InvalidOperationException();
	public bool IsAllStreamPosition => true;
	public bool IsLivePosition => _commitPosition == -1L && _preparePosition == -1L;
	public (long Commit, long Prepare) TFPosition => (_commitPosition, _preparePosition);
	private readonly long _commitPosition;
	private readonly long _preparePosition;

	public PersistentSubscriptionAllStreamPosition(long commitPosition, long preparePosition) {
		_commitPosition = commitPosition;
		_preparePosition = preparePosition;
	}

	public bool Equals(IPersistentSubscriptionStreamPosition? other) {
		if (other == null) throw new InvalidOperationException();
		if (!(other is PersistentSubscriptionAllStreamPosition)) throw new InvalidOperationException();
		return TFPosition.Commit == other.TFPosition.Commit &&
		       TFPosition.Prepare == other.TFPosition.Prepare;
	}

	public int CompareTo(IPersistentSubscriptionStreamPosition? other) {
		if (other == null) throw new InvalidOperationException();
		if (!(other is PersistentSubscriptionAllStreamPosition)) throw new InvalidOperationException();
		if (Equals(other)) return 0;
		if (TFPosition.Commit < other.TFPosition.Commit ||
		    TFPosition.Commit == other.TFPosition.Commit &&
		    TFPosition.Prepare < other.TFPosition.Prepare) return -1;
		return 1;
	}

	public override string ToString() => $"C:{_commitPosition}/P:{_preparePosition}";
}
