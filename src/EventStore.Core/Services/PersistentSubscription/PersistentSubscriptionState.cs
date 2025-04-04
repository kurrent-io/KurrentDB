// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Core.Services.PersistentSubscription;

[Flags]
public enum PersistentSubscriptionState {
	NotReady = 0x00,
	Behind = 0x01,
	OutstandingPageRequest = 0x02,
	ReplayingParkedMessages = 0x04,
	Live = 0x08,
}
