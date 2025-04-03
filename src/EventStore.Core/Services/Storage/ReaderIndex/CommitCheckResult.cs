// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Services.Storage.ReaderIndex;

public struct CommitCheckResult<TStreamId>(
	CommitDecision decision,
	TStreamId eventStreamId,
	long currentVersion,
	long startEventNumber,
	long endEventNumber,
	bool isSoftDeleted,
	long idempotentLogPosition = -1) {
	public readonly CommitDecision Decision = decision;
	public readonly TStreamId EventStreamId = eventStreamId;
	public readonly long CurrentVersion = currentVersion;
	public readonly long StartEventNumber = startEventNumber;
	public readonly long EndEventNumber = endEventNumber;
	public readonly bool IsSoftDeleted = isSoftDeleted;
	public readonly long IdempotentLogPosition = idempotentLogPosition;
}
