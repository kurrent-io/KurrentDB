// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Security.Claims;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.UserManagement;
using EventStore.Core.Services.TimerService;
using NUnit.Framework;

namespace EventStore.Core.Tests.Helpers.IODispatcherTests.ReadEventsTests;

public abstract class with_read_io_dispatcher<TLogFormat, TStreamId> : IHandle<ClientMessage.ReadStreamEventsForward>,
	IHandle<ClientMessage.ReadStreamEventsBackward>,
	IHandle<TimerMessage.Schedule> {
	protected IODispatcher _ioDispatcher;
	protected readonly ClaimsPrincipal _principal = SystemAccounts.System;
	protected readonly SynchronousScheduler _bus = new();

	protected readonly IODispatcherAsync.CancellationScope _cancellationScope = new();

	protected ClientMessage.ReadStreamEventsForward _readForward;
	protected ClientMessage.ReadStreamEventsBackward _readBackward;
	protected TimerMessage.Schedule _timeoutMessage;

	protected readonly int _maxCount = 1;
	protected readonly int _fromEventNumber = 10;
	protected readonly string _eventStreamId = "test";

	[OneTimeSetUp]
	public virtual void TestFixtureSetUp() {
		var _queue = new QueuedHandlerThreadPool(_bus,"TestQueuedHandler", new QueueStatsManager(), new());
		_ioDispatcher = new IODispatcher(_bus, _queue);
		IODispatcherTestHelpers.SubscribeIODispatcher(_ioDispatcher, _bus);
		_bus.Subscribe<ClientMessage.ReadStreamEventsForward>(this);
		_bus.Subscribe<ClientMessage.ReadStreamEventsBackward>(this);
		_bus.Subscribe<TimerMessage.Schedule>(this);
		_queue.Start();
	}

	public virtual void Handle(ClientMessage.ReadStreamEventsForward message) {
		_readForward = message;
	}

	public virtual void Handle(ClientMessage.ReadStreamEventsBackward message) {
		_readBackward = message;
	}

	public virtual void Handle(TimerMessage.Schedule message) {
		_timeoutMessage = message;
	}

	public ClientMessage.ReadStreamEventsForwardCompleted CreateReadStreamEventsForwardCompleted(
		ClientMessage.ReadStreamEventsForward msg) {
		var lastEventNumber = msg.FromEventNumber + 1;
		var nextEventNumber = lastEventNumber + 1;
		var events =
			IODispatcherTestHelpers.CreateResolvedEvent<TLogFormat, TStreamId>(msg.EventStreamId, "event_type", "test", eventNumber: 10);
		var res = new ClientMessage.ReadStreamEventsForwardCompleted(msg.CorrelationId, msg.EventStreamId,
			msg.FromEventNumber,
			msg.MaxCount, ReadStreamResult.Success, events, null, false, String.Empty, nextEventNumber,
			lastEventNumber, false, 0);
		return res;
	}

	public ClientMessage.ReadStreamEventsBackwardCompleted CreateReadStreamEventsBackwardCompleted(
		ClientMessage.ReadStreamEventsBackward msg) {
		var startEventNumber = msg.FromEventNumber;
		var nextEventNumber = startEventNumber - 1;
		var events =
			IODispatcherTestHelpers.CreateResolvedEvent<TLogFormat, TStreamId>(msg.EventStreamId, "event_type", "test", eventNumber: 10);
		var res = new ClientMessage.ReadStreamEventsBackwardCompleted(msg.CorrelationId, msg.EventStreamId,
			msg.FromEventNumber,
			msg.MaxCount, ReadStreamResult.Success, events, null, false, String.Empty, nextEventNumber,
			startEventNumber, false, 0);
		return res;
	}
}
