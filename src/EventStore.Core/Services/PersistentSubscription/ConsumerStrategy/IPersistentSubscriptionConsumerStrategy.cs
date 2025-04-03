// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Data;

namespace EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;

public interface IPersistentSubscriptionConsumerStrategy {
	string Name { get; }

	void ClientAdded(PersistentSubscriptionClient client);

	void ClientRemoved(PersistentSubscriptionClient client);

	ConsumerPushResult PushMessageToClient(OutstandingMessage message);
}
