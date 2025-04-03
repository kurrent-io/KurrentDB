// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.TransactionLog.Scavenging;

public struct IndexExecutionInfo : IEquatable<IndexExecutionInfo> {
	public IndexExecutionInfo(
		bool isMetastream,
		bool isTombstoned,
		DiscardPoint discardPoint) {

		IsMetastream = isMetastream;
		IsTombstoned = isTombstoned;
		DiscardPoint = discardPoint;
	}

	public bool IsMetastream { get; }

	/// <summary>
	/// True when the corresponding original stream is tombstoned
	/// </summary>
	public bool IsTombstoned { get; }

	public DiscardPoint DiscardPoint { get; }

	// avoid the default, reflection based, implementations if we ever need to call these
	public override int GetHashCode() => throw new NotImplementedException();
	public override bool Equals(object other) => throw new NotImplementedException();
	public bool Equals(IndexExecutionInfo other) => throw new NotImplementedException();
}
