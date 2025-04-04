// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

public abstract class TestFixtureWithReadWriteDispatchers :
	EventStore.Core.Tests.Helpers.TestFixtureWithReadWriteDispatchers {
	protected ReaderSubscriptionDispatcher _subscriptionDispatcher;

	[SetUp]
	public void SetUp() {
		_subscriptionDispatcher = new ReaderSubscriptionDispatcher(_bus);
		_bus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.CommittedEventReceived>());
		_bus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.CheckpointSuggested>());
		_bus.Subscribe(_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.EofReached>());
		_bus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.PartitionEofReached>());
		_bus.Subscribe(_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.PartitionDeleted>());
		_bus.Subscribe(_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.ProgressChanged>());
		_bus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.SubscriptionStarted>());
		_bus.Subscribe(_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.NotAuthorized>());
		_bus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.ReaderAssignedReader>());
	}
}
