// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace KurrentDB.Core.Services.PersistentSubscription;

public interface IPersistentSubscriptionPushScheduler {
	static IPersistentSubscriptionPushScheduler NoOp { get; } = new NoOp();
	void SchedulePush();
}

file class NoOp : IPersistentSubscriptionPushScheduler {
	public void SchedulePush() {
	}
}
