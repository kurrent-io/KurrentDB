// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

#nullable enable
using System;

namespace EventStore.Core.Services.PersistentSubscription;

public interface IPersistentSubscriptionCheckpointReader {
	void BeginLoadState(string subscriptionId, Action<string?> onStateLoaded);
}
