// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Data;

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
