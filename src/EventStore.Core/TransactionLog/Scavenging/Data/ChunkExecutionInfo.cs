// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.TransactionLog.Scavenging;

// For ChunkExecutor, which implements maxAge more accurately than the index executor
public struct ChunkExecutionInfo : IEquatable<ChunkExecutionInfo> {
	public ChunkExecutionInfo(
		bool isTombstoned,
		DiscardPoint discardPoint,
		DiscardPoint maybeDiscardPoint,
		TimeSpan? maxAge) {

		IsTombstoned = isTombstoned;
		DiscardPoint = discardPoint;
		MaybeDiscardPoint = maybeDiscardPoint;
		MaxAge = maxAge;
	}

	public bool IsTombstoned { get; }
	public DiscardPoint DiscardPoint { get; }
	public DiscardPoint MaybeDiscardPoint { get; }
	public TimeSpan? MaxAge { get; }

	public bool Equals(ChunkExecutionInfo other) =>
		IsTombstoned == other.IsTombstoned &&
		DiscardPoint == other.DiscardPoint &&
		MaybeDiscardPoint == other.MaybeDiscardPoint &&
		MaxAge == other.MaxAge;

	// avoid the default, reflection based, implementations if we ever need to call these
	public override int GetHashCode() => throw new NotImplementedException();
	public override bool Equals(object other) => throw new NotImplementedException();
}
