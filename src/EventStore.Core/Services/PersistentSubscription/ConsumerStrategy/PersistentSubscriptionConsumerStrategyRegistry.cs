// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Bus;
using EventStore.Core.Index.Hashes;

namespace EventStore.Core.Services.PersistentSubscription.ConsumerStrategy;

public class PersistentSubscriptionConsumerStrategyRegistry {
	private readonly IPublisher _mainQueue;
	private readonly ISubscriber _mainBus;

	private readonly IDictionary<string, IPersistentSubscriptionConsumerStrategyFactory> _factoryLookup =
		new Dictionary<string, IPersistentSubscriptionConsumerStrategyFactory>();

	public PersistentSubscriptionConsumerStrategyRegistry(IPublisher mainQueue, ISubscriber mainBus,
		IReadOnlyList<IPersistentSubscriptionConsumerStrategyFactory> additionalConsumerStrategies) {
		_mainQueue = mainQueue;
		_mainBus = mainBus;
		Register(new DelegatePersistentSubscriptionConsumerStrategyFactory(SystemConsumerStrategies.RoundRobin,
			(subId, queue, bus) => new RoundRobinPersistentSubscriptionConsumerStrategy()));
		Register(new DelegatePersistentSubscriptionConsumerStrategyFactory(
			SystemConsumerStrategies.DispatchToSingle,
			(subId, queue, bus) => new DispatchToSinglePersistentSubscriptionConsumerStrategy()));
		Register(new DelegatePersistentSubscriptionConsumerStrategyFactory(SystemConsumerStrategies.Pinned,
			(subId, queue, bus) => new PinnedPersistentSubscriptionConsumerStrategy(new XXHashUnsafe())));
		Register(new DelegatePersistentSubscriptionConsumerStrategyFactory(SystemConsumerStrategies.PinnedByCorrelation,
			(subId, queue, bus) => new PinnedByCorrelationPersistentSubscriptionConsumerStrategy(new XXHashUnsafe())));

		foreach (var consumerStrategyFactory in additionalConsumerStrategies) {
			Register(consumerStrategyFactory);
		}
	}

	private void Register(IPersistentSubscriptionConsumerStrategyFactory factory) {
		// Note this is designed to replace strategies of the same name to allow overriding.
		_factoryLookup[factory.StrategyName] = factory;
	}

	public IPersistentSubscriptionConsumerStrategy
		GetInstance(string namedConsumerStrategy, string subscriptionId) {
		if (!ValidateStrategy(namedConsumerStrategy)) {
			throw new ArgumentException(
				string.Format("The named consumer strategy '{0}' is unknown.", namedConsumerStrategy),
				"namedConsumerStrategy");
		}

		return _factoryLookup[namedConsumerStrategy].Create(subscriptionId, _mainQueue, _mainBus);
	}

	public bool ValidateStrategy(string name) {
		return _factoryLookup.ContainsKey(name);
	}
}
