// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventStore.Common.Options;
using EventStore.Core.Bus;
using EventStore.Core.Data;
using EventStore.Core.Helpers;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests.Bus.Helpers;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.Util;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Strategies;
using NUnit.Framework;
using ResolvedEvent = EventStore.Projections.Core.Services.Processing.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services;

public class TestFixtureWithProjectionCoreService {
	class GuardBusToTriggerFixingIfUsed : IQueuedHandler, ISubscriber, IPublisher {
		public void Handle(Message message) {
			throw new NotImplementedException();
		}

		public void Publish(Message message) {
			throw new NotImplementedException();
		}

		public void Start() => throw new NotImplementedException();

		public Task Stop() => Task.FromException(new NotImplementedException());

		public void RequestStop() {
			throw new NotImplementedException();
		}

		public void Subscribe<T>(IAsyncHandle<T> handler) where T : Message {
			throw new NotImplementedException();
		}

		public void Unsubscribe<T>(IAsyncHandle<T> handler) where T : Message {
			throw new NotImplementedException();
		}
	}
	public class TestCoreProjection : ICoreProjection {
		public List<EventReaderSubscriptionMessage.CommittedEventReceived> HandledMessages =
			new List<EventReaderSubscriptionMessage.CommittedEventReceived>();

		public void Handle(CoreProjectionProcessingMessage.CheckpointCompleted message) {
			throw new NotImplementedException();
		}

		public void Handle(CoreProjectionProcessingMessage.CheckpointLoaded message) {
			throw new NotImplementedException();
		}


		public void Handle(CoreProjectionProcessingMessage.RestartRequested message) {
			throw new NotImplementedException();
		}

		public void Handle(CoreProjectionProcessingMessage.Failed message) {
			throw new NotImplementedException();
		}

		public void Handle(CoreProjectionProcessingMessage.PrerecordedEventsLoaded message) {
			throw new NotImplementedException();
		}
	}

	protected TestHandler<Message> _consumer;
	protected SynchronousScheduler _bus;
	protected ProjectionCoreService _service;
	protected EventReaderCoreService _readerService;

	private ReaderSubscriptionDispatcher _subscriptionDispatcher;

	protected Guid _workerId;

	[SetUp]
	public virtual void Setup() {
		_consumer = new TestHandler<Message>();
		_bus = new();
		_bus.Subscribe(_consumer);
		ICheckpoint writerCheckpoint = new InMemoryCheckpoint(1000);
		var ioDispatcher = new IODispatcher(_bus, _bus, true);
		_readerService = new EventReaderCoreService(_bus, ioDispatcher, 10, writerCheckpoint,
			runHeadingReader: true, faultOutOfOrderProjections: true);
		_subscriptionDispatcher =
			new ReaderSubscriptionDispatcher(_bus);
		_workerId = Guid.NewGuid();
		var guardBus = new GuardBusToTriggerFixingIfUsed();
		var configuration = new ProjectionsStandardComponents(1, ProjectionType.All, guardBus, guardBus, guardBus, guardBus, true,
			 500, 250, Opts.MaxProjectionStateSizeDefault);
		_service = new ProjectionCoreService(
			_workerId, _bus, _bus, _subscriptionDispatcher, new RealTimeProvider(), ioDispatcher, configuration);
		_bus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.CheckpointSuggested>());
		_bus.Subscribe(_subscriptionDispatcher
			.CreateSubscriber<EventReaderSubscriptionMessage.CommittedEventReceived>());
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

		var instanceCorrelationId = Guid.NewGuid();
		_readerService.Handle(new ReaderCoreServiceMessage.StartReader(instanceCorrelationId));
		_service.Handle(new ProjectionCoreServiceMessage.StartCore(instanceCorrelationId));
	}

	protected IReaderStrategy CreateReaderStrategy() {
		var result = new SourceDefinitionBuilder();
		result.FromAll();
		result.AllEvents();
		return ReaderStrategy.Create(
			"test",
			0,
			result.Build(),
			new RealTimeProvider(),
			stopOnEof: false,
			runAs: null);
	}

	protected static ResolvedEvent CreateEvent() {
		return new ResolvedEvent(
			"test", -1, "test", -1, false, new TFPos(10, 5), new TFPos(10, 5), Guid.NewGuid(), "t", false,
			new byte[0], new byte[0], null, null, default(DateTime));
	}
}
