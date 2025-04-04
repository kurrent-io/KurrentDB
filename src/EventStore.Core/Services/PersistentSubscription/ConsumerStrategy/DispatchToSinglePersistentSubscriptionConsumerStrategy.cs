// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

namespace EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;

class DispatchToSinglePersistentSubscriptionConsumerStrategy : RoundRobinPersistentSubscriptionConsumerStrategy {
	public override string Name {
		get { return SystemConsumerStrategies.DispatchToSingle; }
	}

	public override ConsumerPushResult PushMessageToClient(OutstandingMessage message) {
		for (int i = 0; i < Clients.Count; i++) {
			if (Clients.Peek().Push(message)) {
				return ConsumerPushResult.Sent;
			}

			var c = Clients.Dequeue();
			Clients.Enqueue(c);
		}

		return ConsumerPushResult.NoMoreCapacity;
	}
}
