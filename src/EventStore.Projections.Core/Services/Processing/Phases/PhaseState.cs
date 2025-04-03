// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Projections.Core.Services.Processing.Phases;

public enum PhaseState {
	Unknown,
	Stopped,
	Starting,
	Running,
}

public enum PhaseSubscriptionState {
	Unknown = 0,
	Unsubscribed,
	Subscribing,
	Subscribed,
	Failed
}
