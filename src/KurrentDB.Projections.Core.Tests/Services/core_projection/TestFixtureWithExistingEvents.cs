// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.AwakeReaderService;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Messaging;
using KurrentDB.Projections.Core.Services;
using NUnit.Framework;
using AwakeServiceMessage = KurrentDB.Core.Services.AwakeReaderService.AwakeServiceMessage;

namespace KurrentDB.Projections.Core.Tests.Services.core_projection;

public abstract class TestFixtureWithExistingEvents<TLogFormat, TStreamId>
	: KurrentDB.Core.Tests.Helpers.TestFixtureWithExistingEvents<TLogFormat, TStreamId>,
		IHandle<ProjectionCoreServiceMessage.CoreTick> {
	protected ReaderSubscriptionDispatcher SubscriptionDispatcher;

	private bool _ticksAreHandledImmediately;
	protected AwakeService AwakeService;

	protected override void Given1() {
		base.Given1();
		_ticksAreHandledImmediately = false;
	}

	[SetUp]
	public void SetUp() {
		SubscriptionDispatcher = new(_bus);
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.CommittedEventReceived>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.CheckpointSuggested>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.EofReached>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.PartitionDeleted>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.ProgressChanged>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.SubscriptionStarted>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.NotAuthorized>());
		_bus.Subscribe(SubscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.ReaderAssignedReader>());
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
		string streamId,
		string eventType,
		string data,
		string metadata = null,
		bool isJson = true,
		Guid? correlationId = null) {
		return ClientMessage.WriteEvents.ForSingleEvent(Guid.NewGuid(), correlationId ?? Guid.NewGuid(), GetInputQueue(), false, streamId,
			ExpectedVersion.Any, new Event(Guid.NewGuid(), eventType, isJson, data, metadata), null);
	}
}
