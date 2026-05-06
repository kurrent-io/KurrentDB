// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Messaging;

namespace KurrentDB.Core.Services.PersistentSubscription;

public class PersistentSubscriptionPushScheduler(string subscriptionId, IPublisher bus) : IPersistentSubscriptionPushScheduler {
	private readonly Message _msg = new SubscriptionMessage.PersistentSubscriptionPushToClients(subscriptionId);

	public void SchedulePush() {
		bus.Publish(_msg);
	}
}
