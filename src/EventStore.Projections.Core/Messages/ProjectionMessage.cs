// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Projections.Core.Messages;

// The name of this enum and its members are used for metrics
public enum ProjectionMessage {
	None,
	CoreManagement,
	CoreProcessing,
	CoreStatus,
	EventReaderSubscription,
	FeedReader,
	Management,
	Misc,
	ReaderCoreService,
	ReaderSubscription,
	ReaderSubscriptionManagement,
	ServiceMessage,
	Subsystem,
}
