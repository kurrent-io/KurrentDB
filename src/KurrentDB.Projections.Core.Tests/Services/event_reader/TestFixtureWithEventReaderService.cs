// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using KurrentDB.Core.Tests.Helpers;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader;

public class TestFixtureWithEventReaderService<TLogFormat, TStreamId> : core_projection.TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	protected EventReaderCoreService _readerService;

	protected override void Given1() {
		base.Given1();
		EnableReadAll();
	}

	protected override ManualQueue GiveInputQueue() => new(_bus, _timeProvider);

	[SetUp]
	public void Setup() {
		_bus.Subscribe(_consumer);

		ICheckpoint writerCheckpoint = new InMemoryCheckpoint(1000);
		_readerService = new(GetInputQueue(), _ioDispatcher, 10, writerCheckpoint, runHeadingReader: GivenHeadingReaderRunning(),
			faultOutOfOrderProjections: true);
		SubscriptionDispatcher = new ReaderSubscriptionDispatcher(GetInputQueue());


		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.CheckpointSuggested>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.CommittedEventReceived>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.EofReached>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.ProgressChanged>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.SubscriptionStarted>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.NotAuthorized>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.ReaderAssignedReader>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.Failed>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.SubscribeTimeout>());

		_bus.Subscribe<ReaderCoreServiceMessage.StartReader>(_readerService);
		_bus.Subscribe<ReaderCoreServiceMessage.StopReader>(_readerService);
		_bus.Subscribe<ReaderSubscriptionMessage.CommittedEventDistributed>(_readerService);
		_bus.Subscribe<ReaderSubscriptionMessage.EventReaderEof>(_readerService);
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

	protected virtual bool GivenHeadingReaderRunning() => false;

	protected virtual void GivenAdditionalServices() {
	}

	protected Guid GetReaderId() {
		var readerAssignedMessage = _consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.ReaderAssignedReader>().LastOrDefault();
		Assert.IsNotNull(readerAssignedMessage);
		var reader = readerAssignedMessage.ReaderId;
		return reader;
	}
}
