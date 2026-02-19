// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Core.Services.Storage.ReaderIndex;

public enum CommitDecision {
	Ok,
	ConsistencyCheckFailure,
	Idempotent,

	/// <summary>Some of the events in the stream are idempotent and others are not</summary>
	CorruptedIdempotency,
	InvalidTransaction,
	/// <summary>Idempotent write, but not yet indexed</summary>
	IdempotentNotReady,
}
