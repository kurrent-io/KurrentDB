// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;

namespace EventStore.Core.PluginModel;

public interface IPersistentSubscriptionConsumerStrategyPlugin {
	string Name { get; }

	string Version { get; }

	IPersistentSubscriptionConsumerStrategyFactory GetConsumerStrategyFactory();
}
