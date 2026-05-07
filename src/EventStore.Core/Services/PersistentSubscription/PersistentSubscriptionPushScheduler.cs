// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;

namespace EventStore.Core.Services.PersistentSubscription;

public class PersistentSubscriptionPushScheduler(string subscriptionId, IPublisher bus) : IPersistentSubscriptionPushScheduler {
	private readonly Message _msg = new SubscriptionMessage.PersistentSubscriptionPushToClients(subscriptionId);

	public void SchedulePush() {
		bus.Publish(_msg);
	}
}
