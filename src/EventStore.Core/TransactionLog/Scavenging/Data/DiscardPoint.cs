// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Utils;

namespace EventStore.Core.TransactionLog.Scavenging;

public readonly struct DiscardPoint : IEquatable<DiscardPoint> {
	private DiscardPoint(long firstEventNumberToKeep) {
		if (firstEventNumberToKeep < 0)
			firstEventNumberToKeep = 0;

		FirstEventNumberToKeep = firstEventNumberToKeep;
	}

	public static DiscardPoint DiscardBefore(long eventNumber) =>
		new DiscardPoint(eventNumber);

	public static DiscardPoint DiscardIncluding(long eventNumber) {
		if (eventNumber == long.MaxValue)
			throw new ArgumentOutOfRangeException(
				nameof(eventNumber),
				eventNumber,
				"eventNumber must be less than long.MaxValue");

		return DiscardBefore(eventNumber + 1);
	}

	public static DiscardPoint KeepAll { get; } = DiscardBefore(0);

	public long FirstEventNumberToKeep { get; }

	// Produces a discard point that discards when this OR that discard point would discard
	// i.e. takes the bigger of the two.
	public DiscardPoint Or(DiscardPoint x) =>
		FirstEventNumberToKeep > x.FirstEventNumberToKeep ? this : x;

	public static bool operator <(DiscardPoint x, DiscardPoint y) =>
		x.FirstEventNumberToKeep < y.FirstEventNumberToKeep;

	public static bool operator >(DiscardPoint x, DiscardPoint y) =>
		x.FirstEventNumberToKeep > y.FirstEventNumberToKeep;

	public static bool operator <=(DiscardPoint x, DiscardPoint y) =>
		x.FirstEventNumberToKeep <= y.FirstEventNumberToKeep;

	public static bool operator >=(DiscardPoint x, DiscardPoint y) =>
		x.FirstEventNumberToKeep >= y.FirstEventNumberToKeep;

	public static bool operator ==(DiscardPoint x, DiscardPoint y) =>
		x.FirstEventNumberToKeep == y.FirstEventNumberToKeep;

	public static bool operator !=(DiscardPoint x, DiscardPoint y) =>
		x.FirstEventNumberToKeep != y.FirstEventNumberToKeep;

	public bool Equals(DiscardPoint other) => this == other;

	public override bool Equals(object obj) =>
		obj is DiscardPoint that && this == that;

	public override int GetHashCode() =>
		FirstEventNumberToKeep.GetHashCode();

	public bool ShouldDiscard(long eventNumber) {
		Ensure.Nonnegative(eventNumber, nameof(eventNumber));
		return eventNumber < FirstEventNumberToKeep;
	}

	public override string ToString() {
		if (this == KeepAll)
			return "Keep all";
		else
			return $"Discard before {FirstEventNumberToKeep}";
	}
}
