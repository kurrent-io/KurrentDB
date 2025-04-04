// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.TransactionLog.Scavenging;

public struct MetastreamData : IEquatable<MetastreamData> {
	public static MetastreamData Empty { get; } = new MetastreamData(
		isTombstoned: false,
		discardPoint: DiscardPoint.KeepAll);

	public MetastreamData(
		bool isTombstoned,
		DiscardPoint discardPoint) {

		IsTombstoned = isTombstoned;
		DiscardPoint = discardPoint;
	}

	/// <summary>
	/// True when the corresponding original stream is tombstoned
	/// </summary>
	public bool IsTombstoned { get; }

	public DiscardPoint DiscardPoint { get; }

	public bool Equals(MetastreamData other) =>
		IsTombstoned == other.IsTombstoned &&
		DiscardPoint == other.DiscardPoint;

	// avoid the default, reflection based, implementations if we ever need to call these
	public override int GetHashCode() => throw new NotImplementedException();
	public override bool Equals(object other) => throw new NotImplementedException();
}
