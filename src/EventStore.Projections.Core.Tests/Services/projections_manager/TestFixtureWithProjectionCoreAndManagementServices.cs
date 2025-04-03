// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading.Tasks;
using DotNext;
using EventStore.Common.Options;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.AwakeReaderService;
using EventStore.Core.Services.Monitoring.Stats;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.Util;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Messaging;
using EventStore.Projections.Core.Metrics;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Management;
using EventStore.Projections.Core.Services.Processing;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projections_manager;

public abstract class TestFixtureWithProjectionCoreAndManagementServices<TLogFormat, TStreamId> : core_projection.TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	protected class GuardBusToTriggerFixingIfUsed : IQueuedHandler, IPublisher, ISubscriber {
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

		public QueueStats GetStatistics() {
			throw new NotImplementedException();
		}

		public void Subscribe<T>(IAsyncHandle<T> handler) where T : Message {
			throw new NotImplementedException();
		}

		public void Unsubscribe<T>(IAsyncHandle<T> handler) where T : Message {
			throw new NotImplementedException();
		}
	}
	protected ProjectionManager _manager;
	protected ProjectionManagerMessageDispatcher _managerMessageDispatcher;
	private bool _initializeSystemProjections;
	protected Tuple<SynchronousScheduler, IPublisher, SynchronousScheduler, Guid>[] _processingQueues;
	private ProjectionCoreCoordinator _coordinator;

	protected override void Given1() {
		base.Given1();
		_initializeSystemProjections = GivenInitializeSystemProjections();
		if (!_initializeSystemProjections) {
			ExistingEvent(ProjectionNamesBuilder.ProjectionsRegistrationStream,
				ProjectionEventTypes.ProjectionsInitialized, "", "");
		}
	}

	protected virtual bool GivenInitializeSystemProjections() {
		return false;
	}

	protected override ManualQueue GiveInputQueue() {
		return new ManualQueue(_bus, _timeProvider);
	}

	[SetUp]
	public void Setup() {
		//TODO: this became an integration test - proper ProjectionCoreService and ProjectionManager testing is required as well
		_bus.Subscribe(_consumer);

		_processingQueues = GivenProcessingQueues();
		var queues = _processingQueues.ToDictionary(v => v.Item4, v => v.Item1.As<IPublisher>());
		_managerMessageDispatcher = new ProjectionManagerMessageDispatcher(queues);
		_manager = new ProjectionManager(
			GetInputQueue(),
			GetInputQueue(),
			queues,
			_timeProvider,
			ProjectionType.All,
			_ioDispatcher,
			TimeSpan.FromMinutes(Opts.ProjectionsQueryExpiryDefault),
			IProjectionTracker.NoOp,
			_initializeSystemProjections);

		_coordinator = new ProjectionCoreCoordinator(
			ProjectionType.All,
			queues.Values.ToArray(),
			_bus);

		_bus.Subscribe<ProjectionManagementMessage.Internal.CleanupExpired>(_manager);
		_bus.Subscribe<ProjectionManagementMessage.Internal.Deleted>(_manager);
		_bus.Subscribe<CoreProjectionStatusMessage.Started>(_manager);
		_bus.Subscribe<CoreProjectionStatusMessage.Stopped>(_manager);
		_bus.Subscribe<CoreProjectionStatusMessage.Prepared>(_manager);
		_bus.Subscribe<CoreProjectionStatusMessage.Faulted>(_manager);
		_bus.Subscribe<CoreProjectionStatusMessage.StateReport>(_manager);
		_bus.Subscribe<CoreProjectionStatusMessage.ResultReport>(_manager);
		_bus.Subscribe<CoreProjectionStatusMessage.StatisticsReport>(_manager);
		_bus.Subscribe<ProjectionManagementMessage.Command.Post>(_manager);
		_bus.Subscribe<ProjectionManagementMessage.Command.PostBatch>(_manager);
		_bus.Subscribe<ProjectionManagementMessage.Command.UpdateQuery>(_manager);
		_bus.Subscribe<ProjectionManagementMessage.Command.GetQuery>(_manager);
		_bus.Subscribe<ProjectionManagementMessage.Command.Delete>(_manager);
		_bus.Subscribe<ProjectionManagementMessage.Command.GetStatistics>(_manager);
		_bus.Subscribe<ProjectionManagementMessage.Command.GetState>(_manager);
		_bus.Subscribe<ProjectionManagementMessage.Command.GetResult>(_manager);
		_bus.Subscribe<ProjectionManagementMessage.Command.Disable>(_manager);
		_bus.Subscribe<ProjectionManagementMessage.Command.Enable>(_manager);
		_bus.Subscribe<ProjectionManagementMessage.Command.Abort>(_manager);
		_bus.Subscribe<ProjectionManagementMessage.Command.SetRunAs>(_manager);
		_bus.Subscribe<ProjectionManagementMessage.Command.Reset>(_manager);
		_bus.Subscribe<ClientMessage.WriteEventsCompleted>(_manager);
		_bus.Subscribe<ClientMessage.ReadStreamEventsForwardCompleted>(_manager);
		_bus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(_manager);
		_bus.Subscribe<ClientMessage.DeleteStreamCompleted>(_manager);
		_bus.Subscribe<ProjectionSubsystemMessage.StartComponents>(_manager);
		_bus.Subscribe<ProjectionSubsystemMessage.StopComponents>(_manager);
		_bus.Subscribe<ProjectionSubsystemMessage.StartComponents>(_coordinator);
		_bus.Subscribe<ProjectionSubsystemMessage.StopComponents>(_coordinator);

		if (GetInputQueue() != _processingQueues.First().Item2) {
			_bus.Subscribe<CoreProjectionManagementControlMessage>(
				_managerMessageDispatcher);
		}

		foreach (var q in _processingQueues)
			SetUpCoreServices(q.Item4, q.Item1, q.Item2, q.Item3);

		//Given();
		WhenLoop();
	}

	protected virtual Tuple<SynchronousScheduler, IPublisher, SynchronousScheduler, Guid>[] GivenProcessingQueues() {
		return new[] {
			Tuple.Create(_bus, GetInputQueue(), (SynchronousScheduler)null, Guid.NewGuid())
		};
	}

	private void SetUpCoreServices(
		Guid workerId,
		SynchronousScheduler bus,
		IPublisher inputQueue,
		SynchronousScheduler output_) {
		var output = (output_ ?? inputQueue);
		ICheckpoint writerCheckpoint = new InMemoryCheckpoint(1000);
		var readerService = new EventReaderCoreService(
			output,
			_ioDispatcher,
			10,
			writerCheckpoint,
			runHeadingReader: true, faultOutOfOrderProjections: true);
		_subscriptionDispatcher = new ReaderSubscriptionDispatcher(inputQueue);

		bus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.CheckpointSuggested>());
		bus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.CommittedEventReceived>());
		bus.Subscribe(_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.EofReached>());
		bus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.PartitionEofReached>());
		bus.Subscribe(_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.PartitionDeleted>());
		bus.Subscribe(_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.ProgressChanged>());
		bus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.SubscriptionStarted>());
		bus.Subscribe(_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.NotAuthorized>());
		bus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.ReaderAssignedReader>());

		var ioDispatcher = new IODispatcher(output, inputQueue, true);

		var guardBus = new GuardBusToTriggerFixingIfUsed();
		var configuration = new ProjectionsStandardComponents(1, ProjectionType.All, guardBus, guardBus, guardBus, guardBus, true,
			500, 250, Opts.MaxProjectionStateSizeDefault);
		var coreService = new ProjectionCoreService(
			workerId,
			inputQueue,
			output,
			_subscriptionDispatcher,
			_timeProvider,
			ioDispatcher,
			configuration);

		bus.Subscribe<CoreProjectionManagementMessage.CreateAndPrepare>(coreService);
		bus.Subscribe<CoreProjectionManagementMessage.CreatePrepared>(coreService);
		bus.Subscribe<CoreProjectionManagementMessage.Dispose>(coreService);
		bus.Subscribe<CoreProjectionManagementMessage.Start>(coreService);
		bus.Subscribe<CoreProjectionManagementMessage.LoadStopped>(coreService);
		bus.Subscribe<CoreProjectionManagementMessage.Stop>(coreService);
		bus.Subscribe<CoreProjectionManagementMessage.Kill>(coreService);
		bus.Subscribe<CoreProjectionManagementMessage.GetState>(coreService);
		bus.Subscribe<CoreProjectionManagementMessage.GetResult>(coreService);
		bus.Subscribe<CoreProjectionProcessingMessage.CheckpointCompleted>(coreService);
		bus.Subscribe<CoreProjectionProcessingMessage.CheckpointLoaded>(coreService);
		bus.Subscribe<CoreProjectionProcessingMessage.PrerecordedEventsLoaded>(coreService);
		bus.Subscribe<CoreProjectionProcessingMessage.RestartRequested>(coreService);
		bus.Subscribe<CoreProjectionProcessingMessage.Failed>(coreService);
		bus.Subscribe<ClientMessage.ReadStreamEventsForwardCompleted>(ioDispatcher.ForwardReader);
		bus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(ioDispatcher.BackwardReader);
		bus.Subscribe<ClientMessage.NotHandled>(ioDispatcher.BackwardReader);
		bus.Subscribe<ClientMessage.WriteEventsCompleted>(ioDispatcher.Writer);
		bus.Subscribe<ClientMessage.DeleteStreamCompleted>(ioDispatcher.StreamDeleter);
		bus.Subscribe<IODispatcherDelayedMessage>(ioDispatcher.Awaker);
		bus.Subscribe<IODispatcherDelayedMessage>(ioDispatcher);
		bus.Subscribe<ProjectionCoreServiceMessage.StartCore>(coreService);
		bus.Subscribe<ProjectionCoreServiceMessage.StopCore>(coreService);
		bus.Subscribe<ReaderCoreServiceMessage.StartReader>(readerService);
		bus.Subscribe<ReaderCoreServiceMessage.StopReader>(readerService);
		bus.Subscribe<ProjectionCoreServiceMessage.CoreTick>(coreService);
		bus.Subscribe<ReaderSubscriptionMessage.CommittedEventDistributed>(readerService);
		bus.Subscribe<ReaderSubscriptionMessage.EventReaderEof>(readerService);
		bus.Subscribe<ReaderSubscriptionMessage.EventReaderPartitionEof>(readerService);
		bus.Subscribe<ReaderSubscriptionMessage.EventReaderPartitionDeleted>(readerService);
		bus.Subscribe<ReaderSubscriptionMessage.EventReaderNotAuthorized>(readerService);
		bus.Subscribe<ReaderSubscriptionMessage.EventReaderIdle>(readerService);
		bus.Subscribe<ReaderSubscriptionMessage.EventReaderStarting>(readerService);
		bus.Subscribe<ReaderSubscriptionManagement.Pause>(readerService);
		bus.Subscribe<ReaderSubscriptionManagement.Resume>(readerService);
		bus.Subscribe<ReaderSubscriptionManagement.Subscribe>(readerService);
		bus.Subscribe<ReaderSubscriptionManagement.Unsubscribe>(readerService);

		if (output_ != null) {
			bus.Subscribe(new UnwrapEnvelopeHandler());
			output_.Subscribe(Forwarder.Create<CoreProjectionStatusMessage.StateReport>(GetInputQueue()));
			output_.Subscribe(Forwarder.Create<CoreProjectionStatusMessage.ResultReport>(GetInputQueue()));
			output_.Subscribe(Forwarder.Create<CoreProjectionStatusMessage.StatisticsReport>(GetInputQueue()));
			output_.Subscribe(Forwarder.Create<CoreProjectionStatusMessage.Started>(GetInputQueue()));
			output_.Subscribe(Forwarder.Create<CoreProjectionStatusMessage.Stopped>(GetInputQueue()));
			output_.Subscribe(Forwarder.Create<CoreProjectionStatusMessage.Faulted>(GetInputQueue()));
			output_.Subscribe(Forwarder.Create<CoreProjectionStatusMessage.Prepared>(GetInputQueue()));
			output_.Subscribe(
				Forwarder.Create<ProjectionManagementMessage.Command.ControlMessage>(GetInputQueue()));
			output_.Subscribe(Forwarder.Create<AwakeServiceMessage.SubscribeAwake>(GetInputQueue()));
			output_.Subscribe(Forwarder.Create<AwakeServiceMessage.UnsubscribeAwake>(GetInputQueue()));
			output_.Subscribe(Forwarder.Create<Message>(inputQueue)); // forward all

			var forwarder = new RequestResponseQueueForwarder(
				inputQueue: inputQueue,
				externalRequestQueue: GetInputQueue());
			// forwarded messages
			output_.Subscribe<ClientMessage.ReadEvent>(forwarder);
			output_.Subscribe<ClientMessage.ReadStreamEventsBackward>(forwarder);
			output_.Subscribe<ClientMessage.ReadStreamEventsForward>(forwarder);
			output_.Subscribe<ClientMessage.ReadAllEventsForward>(forwarder);
			output_.Subscribe<ClientMessage.WriteEvents>(forwarder);
		}
	}
}
