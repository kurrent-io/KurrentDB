// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.TransactionLog.Scavenging;

public enum DiscardDecision {
	None,
	Discard,
	// calculator cannot tell conclusively, depends on record timestamp that it only has
	// approximately. chunks can tell accurately because they have the precise timestamp.
	// index will just keep such indexentries.
	MaybeDiscard,
	Keep,
	AlreadyDiscarded,
}
