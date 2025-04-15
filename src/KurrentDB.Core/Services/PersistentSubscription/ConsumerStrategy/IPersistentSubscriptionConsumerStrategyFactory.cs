// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Bus;

namespace KurrentDB.Core.Services.PersistentSubscription.ConsumerStrategy;

public interface IPersistentSubscriptionConsumerStrategyFactory {
	string StrategyName { get; }

	IPersistentSubscriptionConsumerStrategy
		Create(string subscriptionId, IPublisher mainQueue, ISubscriber mainBus);
}
