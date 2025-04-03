// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.event_reader;

public class TestFixtureWithEventReaderService<TLogFormat, TStreamId> : core_projection.TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	protected EventReaderCoreService _readerService;

	protected override void Given1() {
		base.Given1();
		EnableReadAll();
	}

	protected override ManualQueue GiveInputQueue() {
		return new ManualQueue(_bus, _timeProvider);
	}

	[SetUp]
	public void Setup() {
		_bus.Subscribe(_consumer);

		ICheckpoint writerCheckpoint = new InMemoryCheckpoint(1000);
		_readerService = new EventReaderCoreService(
			GetInputQueue(), _ioDispatcher, 10, writerCheckpoint, runHeadingReader: GivenHeadingReaderRunning(),
			faultOutOfOrderProjections: true);
		_subscriptionDispatcher = new ReaderSubscriptionDispatcher(GetInputQueue());


		_bus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.CheckpointSuggested>());
		_bus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.CommittedEventReceived>());
		_bus.Subscribe(_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.EofReached>());
		_bus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.PartitionEofReached>());
		_bus.Subscribe(_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.ProgressChanged>());
		_bus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.SubscriptionStarted>());
		_bus.Subscribe(_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.NotAuthorized>());
		_bus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.ReaderAssignedReader>());
		_bus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.Failed>());
		_bus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.SubscribeTimeout>());

		_bus.Subscribe<ReaderCoreServiceMessage.StartReader>(_readerService);
		_bus.Subscribe<ReaderCoreServiceMessage.StopReader>(_readerService);
		_bus.Subscribe<ReaderSubscriptionMessage.CommittedEventDistributed>(_readerService);
		_bus.Subscribe<ReaderSubscriptionMessage.EventReaderEof>(_readerService);
		_bus.Subscribe<ReaderSubscriptionMessage.EventReaderPartitionEof>(_readerService);
		_bus.Subscribe<ReaderSubscriptionMessage.EventReaderPartitionDeleted>(_readerService);
		_bus.Subscribe<ReaderSubscriptionMessage.EventReaderNotAuthorized>(_readerService);
		_bus.Subscribe<ReaderSubscriptionMessage.EventReaderIdle>(_readerService);
		_bus.Subscribe<ReaderSubscriptionMessage.EventReaderStarting>(_readerService);
		_bus.Subscribe<ReaderSubscriptionManagement.Pause>(_readerService);
		_bus.Subscribe<ReaderSubscriptionManagement.Resume>(_readerService);
		_bus.Subscribe<ReaderSubscriptionManagement.Subscribe>(_readerService);
		_bus.Subscribe<ReaderSubscriptionManagement.Unsubscribe>(_readerService);

		GivenAdditionalServices();

		_bus.Publish(new ReaderCoreServiceMessage.StartReader(Guid.NewGuid()));

		WhenLoop();
	}

	protected virtual bool GivenHeadingReaderRunning() {
		return false;
	}

	protected virtual void GivenAdditionalServices() {
	}

	protected Guid GetReaderId() {
		var readerAssignedMessage =
			_consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.ReaderAssignedReader>().LastOrDefault();
		Assert.IsNotNull(readerAssignedMessage);
		var reader = readerAssignedMessage.ReaderId;
		return reader;
	}
}
