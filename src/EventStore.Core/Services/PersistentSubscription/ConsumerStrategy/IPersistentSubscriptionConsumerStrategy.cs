// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

namespace EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;

public interface IPersistentSubscriptionConsumerStrategy {
	string Name { get; }

	void ClientAdded(PersistentSubscriptionClient client);

	void ClientRemoved(PersistentSubscriptionClient client);

	ConsumerPushResult PushMessageToClient(OutstandingMessage message);
}
