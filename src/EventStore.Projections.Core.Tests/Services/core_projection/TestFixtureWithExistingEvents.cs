// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.AwakeReaderService;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Messaging;
using EventStore.Projections.Core.Services;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

public abstract class TestFixtureWithExistingEvents<TLogFormat, TStreamId> : EventStore.Core.Tests.Helpers.TestFixtureWithExistingEvents<TLogFormat, TStreamId>,
	IHandle<ProjectionCoreServiceMessage.CoreTick> {
	protected ReaderSubscriptionDispatcher _subscriptionDispatcher;

	private bool _ticksAreHandledImmediately;
	protected AwakeService AwakeService;

	protected override void Given1() {
		base.Given1();
		_ticksAreHandledImmediately = false;
	}

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
		_bus.Subscribe<ProjectionCoreServiceMessage.CoreTick>(this);

		AwakeService = new AwakeService();
		_bus.Subscribe<StorageMessage.EventCommitted>(AwakeService);
		_bus.Subscribe<StorageMessage.TfEofAtNonCommitRecord>(AwakeService);
		_bus.Subscribe<AwakeServiceMessage.SubscribeAwake>(AwakeService);
		_bus.Subscribe<AwakeServiceMessage.UnsubscribeAwake>(AwakeService);
		_bus.Subscribe(new UnwrapEnvelopeHandler());
	}

	public void Handle(ProjectionCoreServiceMessage.CoreTick message) {
		if (_ticksAreHandledImmediately)
			message.Action();
	}

	protected void TicksAreHandledImmediately() {
		_ticksAreHandledImmediately = true;
	}

	protected ClientMessage.WriteEvents CreateWriteEvent(
		string streamId, string eventType, string data, string metadata = null, bool isJson = true,
		Guid? correlationId = null) {
		return new ClientMessage.WriteEvents(
			Guid.NewGuid(), correlationId ?? Guid.NewGuid(), GetInputQueue(), false, streamId,
			ExpectedVersion.Any, new Event(Guid.NewGuid(), eventType, isJson, data, metadata), null);
	}
}
