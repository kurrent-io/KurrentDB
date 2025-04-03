// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Services.PersistentSubscription;

public enum NakAction {
	Unknown = 0,
	Park = 1,
	Retry = 2,
	Skip = 3,
	Stop = 4
}
