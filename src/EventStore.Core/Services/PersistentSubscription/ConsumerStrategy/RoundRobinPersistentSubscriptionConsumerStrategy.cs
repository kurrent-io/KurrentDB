// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;

namespace EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;

class RoundRobinPersistentSubscriptionConsumerStrategy : IPersistentSubscriptionConsumerStrategy {
	protected readonly Queue<PersistentSubscriptionClient> Clients = new Queue<PersistentSubscriptionClient>();

	public virtual string Name {
		get { return SystemConsumerStrategies.RoundRobin; }
	}

	public void ClientAdded(PersistentSubscriptionClient client) {
		Clients.Enqueue(client);
	}

	public void ClientRemoved(PersistentSubscriptionClient client) {
		if (!Clients.Contains(client)) {
			throw new InvalidOperationException("Only added clients can be removed.");
		}

		var temp = Clients.ToList();
		var indexOf = temp.IndexOf(client);
		temp.RemoveAt(indexOf);
		Clients.Clear();
		foreach (var persistentSubscriptionClient in temp) {
			Clients.Enqueue(persistentSubscriptionClient);
		}
	}

	public virtual ConsumerPushResult PushMessageToClient(OutstandingMessage message) {
		for (int i = 0; i < Clients.Count; i++) {
			var c = Clients.Dequeue();
			var pushed = c.Push(message);
			Clients.Enqueue(c);
			if (pushed) {
				return ConsumerPushResult.Sent;
			}
		}

		return ConsumerPushResult.NoMoreCapacity;
	}
}
