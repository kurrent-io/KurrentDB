// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Core.Services.PersistentSubscription;

// Outcome of a request to replay or truncate parked messages.
public enum ParkedMessageOperationResult {
	// Rejected because the subscription has not finished initializing.
	NotReady,

	// The operation was accepted and started.
	Started,

	// Rejected because a replay or truncate is already in progress.
	AlreadyInProgress,
}
