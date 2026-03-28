// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Core.Services.Storage.ReaderIndex;

public readonly struct CommitCheckResult<TStreamId>(
	CommitDecision decision, // it is ok/not ok/idempotent etc
	int eventCount,          // to write this many events
	TStreamId eventStreamId, // to this stream
	long expectedVersion,    // at this expected version.
	long currentVersion,
	long startEventNumber,
	long endEventNumber,
	bool? isSoftDeleted,
	long idempotentLogPosition = -1) {

	public readonly CommitDecision Decision = decision;
	public readonly int EventCount = eventCount;
	public readonly TStreamId EventStreamId = eventStreamId;
	public readonly long ExpectedVersion = expectedVersion;
	public readonly long CurrentVersion = currentVersion;
	public readonly long StartEventNumber = startEventNumber;
	public readonly long EndEventNumber = endEventNumber;

	/// <summary>
	/// `true` => known to be soft deleted.
	/// `false` => known not to be soft deleted.
	/// `null` => soft deleted state was not checked.
	/// </summary>
	public readonly bool? IsSoftDeleted = isSoftDeleted;

	public readonly long IdempotentLogPosition = idempotentLogPosition;

	/// <summary>
	/// Whether this stream has events to write (as opposed to a check-only stream).
	/// </summary>
	public bool HasEventsToWrite => EventCount > 0;
}
