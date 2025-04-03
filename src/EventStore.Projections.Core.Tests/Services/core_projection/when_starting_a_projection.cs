// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Services.UserManagement;
using EventStore.Core.Tests.Bus.Helpers;
using EventStore.Core.Util;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Strategies;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Data.ReadStreamResult;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.core_projection;

[TestFixture]
public class when_starting_a_projection {
	private const string _projectionStateStream = "$projections-projection-result";
	private const string _projectionCheckpointStream = "$projections-projection-checkpoint";
	private CoreProjection _coreProjection;
	private SynchronousScheduler _bus;
	private TestHandler<ClientMessage.ReadStreamEventsBackward> _listEventsHandler;
	private IODispatcher _ioDispatcher;
	private ReaderSubscriptionDispatcher _subscriptionDispatcher;
	private ProjectionConfig _projectionConfig;

	[SetUp]
	public void setup() {
		_bus = new();
		_listEventsHandler = new TestHandler<ClientMessage.ReadStreamEventsBackward>();
		_bus.Subscribe(_listEventsHandler);
		_ioDispatcher = new IODispatcher(_bus, _bus, true);
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
		_bus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(_ioDispatcher.BackwardReader);
		_bus.Subscribe<ClientMessage.NotHandled>(_ioDispatcher.BackwardReader);
		_bus.Subscribe(_ioDispatcher.ForwardReader);
		_bus.Subscribe(_ioDispatcher.Writer);
		_bus.Subscribe<IODispatcherDelayedMessage>(_ioDispatcher);
		_bus.Subscribe<ClientMessage.NotHandled>(_ioDispatcher);
		IProjectionStateHandler projectionStateHandler = new FakeProjectionStateHandler();
		_projectionConfig =
			new ProjectionConfig(null, 5, 10, 1000, 250, true, true, false, false, true, 10000, 1, null);
		var version = new ProjectionVersion(1, 0, 0);
		var projectionProcessingStrategy = new ContinuousProjectionProcessingStrategy(
			"projection", version, projectionStateHandler, _projectionConfig,
			projectionStateHandler.GetSourceDefinition(), null, _subscriptionDispatcher, true, Opts.MaxProjectionStateSizeDefault);
		_coreProjection = projectionProcessingStrategy.Create(
			Guid.NewGuid(),
			_bus,
			Guid.NewGuid(),
			SystemAccounts.System,
			_bus,
			_ioDispatcher,
			_subscriptionDispatcher,
			new RealTimeProvider());
		_coreProjection.Start();
	}

	[Test]
	public void should_request_state_snapshot() {
		Assert.IsTrue(_listEventsHandler.HandledMessages.Count == 1);
	}

	[Test]
	public void should_request_state_snapshot_on_correct_stream() {
		Assert.AreEqual(_projectionCheckpointStream, _listEventsHandler.HandledMessages[0].EventStreamId);
	}

	[Test]
	public async Task should_accept_no_event_stream_response() {
		await _bus.DispatchAsync(
			new ClientMessage.ReadStreamEventsBackwardCompleted(
				_listEventsHandler.HandledMessages[0].CorrelationId,
				_listEventsHandler.HandledMessages[0].EventStreamId, 100, 100, ReadStreamResult.NoStream,
				new ResolvedEvent[0], null, false, string.Empty, -1, -1, true, 1000));
	}

	[Test]
	public async Task should_accept_events_not_found_response() {
		await _bus.DispatchAsync(
			new ClientMessage.ReadStreamEventsBackwardCompleted(
				_listEventsHandler.HandledMessages[0].CorrelationId,
				_listEventsHandler.HandledMessages[0].EventStreamId, 100, 100, ReadStreamResult.Success,
				new ResolvedEvent[0], null, false, string.Empty, -1, -1, false, 1000));
	}
}
