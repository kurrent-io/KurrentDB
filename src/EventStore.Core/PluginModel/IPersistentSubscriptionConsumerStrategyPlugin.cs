// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Services.PersistentSubscription;
using EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;

namespace EventStore.Core.PluginModel;

public interface IPersistentSubscriptionConsumerStrategyPlugin {
	string Name { get; }

	string Version { get; }

	IPersistentSubscriptionConsumerStrategyFactory GetConsumerStrategyFactory();
}
