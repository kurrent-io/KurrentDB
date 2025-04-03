// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Common.Options;
using EventStore.Core.Bus;
using EventStore.Common.Utils;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Projections.Core.EventReaders.Feeds;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services;
using EventStore.Projections.Core.Services.Processing;

namespace EventStore.Projections.Core;

public class ProjectionWorkerNode {
	private readonly ProjectionType _runProjections;
	private readonly ProjectionCoreService _projectionCoreService;
	private readonly ISubscriber _coreOutputBus;
	private readonly EventReaderCoreService _eventReaderCoreService;

	private readonly ReaderSubscriptionDispatcher _subscriptionDispatcher;

	private readonly FeedReaderService _feedReaderService;
	private readonly IODispatcher _ioDispatcher;
	private readonly IPublisher _leaderOutputQueue;

	public ProjectionWorkerNode(
		Guid workerId,
		TFChunkDbConfig dbConfig,
		IPublisher inputQueue,
		IPublisher outputQueue,
		ISubscriber outputBus,
		ITimeProvider timeProvider,
		ProjectionType runProjections,
		bool faultOutOfOrderProjections,
		IPublisher leaderOutputQueue,
		ProjectionsStandardComponents configuration) {
		_runProjections = runProjections;
		Ensure.NotNull(dbConfig, "dbConfig");

		_leaderOutputQueue = leaderOutputQueue;
		_coreOutputBus = outputBus;

		_subscriptionDispatcher = new ReaderSubscriptionDispatcher(outputQueue);

		_ioDispatcher = new IODispatcher(outputQueue, inputQueue, true);
		_eventReaderCoreService = new EventReaderCoreService(
			outputQueue,
			_ioDispatcher,
			10,
			dbConfig.WriterCheckpoint,
			runHeadingReader: runProjections >= ProjectionType.System,
			faultOutOfOrderProjections: faultOutOfOrderProjections);

		_feedReaderService = new FeedReaderService(_subscriptionDispatcher, timeProvider);
		if (runProjections >= ProjectionType.System) {
			_projectionCoreService = new ProjectionCoreService(
				workerId,
				inputQueue,
				outputQueue,
				_subscriptionDispatcher,
				timeProvider,
				_ioDispatcher,
				configuration);
		}
	}

	public ISubscriber CoreOutputBus {
		get { return _coreOutputBus;  }
	}

	public void SetupMessaging(ISubscriber coreInputBus) {
		coreInputBus.Subscribe(_subscriptionDispatcher
			.CreateSubscriber<EventReaderSubscriptionMessage.CheckpointSuggested>());
		coreInputBus.Subscribe(_subscriptionDispatcher
			.CreateSubscriber<EventReaderSubscriptionMessage.CommittedEventReceived>());
		coreInputBus.Subscribe(
			_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.EofReached>());
		coreInputBus.Subscribe(_subscriptionDispatcher
			.CreateSubscriber<EventReaderSubscriptionMessage.PartitionEofReached>());
		coreInputBus.Subscribe(_subscriptionDispatcher
			.CreateSubscriber<EventReaderSubscriptionMessage.PartitionDeleted>());
		coreInputBus.Subscribe(_subscriptionDispatcher
			.CreateSubscriber<EventReaderSubscriptionMessage.ProgressChanged>());
		coreInputBus.Subscribe(_subscriptionDispatcher
			.CreateSubscriber<EventReaderSubscriptionMessage.SubscriptionStarted>());
		coreInputBus.Subscribe(_subscriptionDispatcher
			.CreateSubscriber<EventReaderSubscriptionMessage.NotAuthorized>());
		coreInputBus.Subscribe(_subscriptionDispatcher
			.CreateSubscriber<EventReaderSubscriptionMessage.ReaderAssignedReader>());
		coreInputBus.Subscribe(_subscriptionDispatcher
			.CreateSubscriber<EventReaderSubscriptionMessage.SubscribeTimeout>());
		coreInputBus.Subscribe(_subscriptionDispatcher.CreateSubscriber<EventReaderSubscriptionMessage.Failed>());

		coreInputBus.Subscribe(_feedReaderService);

		if (_runProjections >= ProjectionType.System) {
			coreInputBus.Subscribe<ProjectionCoreServiceMessage.StartCore>(_projectionCoreService);
			coreInputBus.Subscribe<ProjectionCoreServiceMessage.StopCore>(_projectionCoreService);
			coreInputBus.Subscribe<ProjectionCoreServiceMessage.StopCoreTimeout>(_projectionCoreService);
			coreInputBus.Subscribe<ProjectionCoreServiceMessage.CoreTick>(_projectionCoreService);
			coreInputBus.Subscribe<CoreProjectionManagementMessage.CreateAndPrepare>(_projectionCoreService);
			coreInputBus.Subscribe<CoreProjectionManagementMessage.CreatePrepared>(_projectionCoreService);
			coreInputBus.Subscribe<CoreProjectionManagementMessage.Dispose>(_projectionCoreService);
			coreInputBus.Subscribe<CoreProjectionManagementMessage.Start>(_projectionCoreService);
			coreInputBus.Subscribe<CoreProjectionManagementMessage.LoadStopped>(_projectionCoreService);
			coreInputBus.Subscribe<CoreProjectionManagementMessage.Stop>(_projectionCoreService);
			coreInputBus.Subscribe<CoreProjectionManagementMessage.Kill>(_projectionCoreService);
			coreInputBus.Subscribe<CoreProjectionManagementMessage.GetState>(_projectionCoreService);
			coreInputBus.Subscribe<CoreProjectionManagementMessage.GetResult>(_projectionCoreService);
			coreInputBus.Subscribe<ClientMessage.ReadStreamEventsForwardCompleted>(_ioDispatcher.ForwardReader);
			coreInputBus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(_ioDispatcher.BackwardReader);
			coreInputBus.Subscribe<ClientMessage.NotHandled>(_ioDispatcher.BackwardReader);
			coreInputBus.Subscribe<ClientMessage.ReadEventCompleted>(_ioDispatcher.EventReader);
			coreInputBus.Subscribe<ClientMessage.WriteEventsCompleted>(_ioDispatcher.Writer);
			coreInputBus.Subscribe<ClientMessage.DeleteStreamCompleted>(_ioDispatcher.StreamDeleter);
			coreInputBus.Subscribe<IODispatcherDelayedMessage>(_ioDispatcher.Awaker);
			coreInputBus.Subscribe<IODispatcherDelayedMessage>(_ioDispatcher);
			coreInputBus.Subscribe<ClientMessage.NotHandled>(_ioDispatcher);
			coreInputBus.Subscribe<CoreProjectionProcessingMessage.CheckpointCompleted>(_projectionCoreService);
			coreInputBus.Subscribe<CoreProjectionProcessingMessage.CheckpointLoaded>(_projectionCoreService);
			coreInputBus.Subscribe<CoreProjectionProcessingMessage.PrerecordedEventsLoaded>(_projectionCoreService);
			coreInputBus.Subscribe<CoreProjectionProcessingMessage.RestartRequested>(_projectionCoreService);
			coreInputBus.Subscribe<CoreProjectionProcessingMessage.Failed>(_projectionCoreService);
			coreInputBus.Subscribe<CoreProjectionStatusMessage.Suspended>(_projectionCoreService);
			//NOTE: message forwarding is set up outside (for Read/Write events)

			// Forward messages back to projection manager
			coreInputBus.Subscribe(
				Forwarder.Create<ProjectionManagementMessage.Command.ControlMessage>(_leaderOutputQueue));
			coreInputBus.Subscribe(
				Forwarder.Create<CoreProjectionStatusMessage.CoreProjectionStatusMessageBase>(_leaderOutputQueue));
			coreInputBus.Subscribe(
				Forwarder.Create<CoreProjectionStatusMessage.DataReportBase>(_leaderOutputQueue));
		}

		coreInputBus.Subscribe<ReaderCoreServiceMessage.StartReader>(_eventReaderCoreService);
		coreInputBus.Subscribe<ReaderCoreServiceMessage.StopReader>(_eventReaderCoreService);
		coreInputBus.Subscribe<ReaderSubscriptionManagement.Subscribe>(_eventReaderCoreService);
		coreInputBus.Subscribe<ReaderSubscriptionManagement.Unsubscribe>(_eventReaderCoreService);
		coreInputBus.Subscribe<ReaderSubscriptionManagement.Pause>(_eventReaderCoreService);
		coreInputBus.Subscribe<ReaderSubscriptionManagement.Resume>(_eventReaderCoreService);
		coreInputBus.Subscribe<ReaderSubscriptionMessage.CommittedEventDistributed>(_eventReaderCoreService);
		coreInputBus.Subscribe<ReaderSubscriptionMessage.EventReaderIdle>(_eventReaderCoreService);
		coreInputBus.Subscribe<ReaderSubscriptionMessage.EventReaderStarting>(_eventReaderCoreService);
		coreInputBus.Subscribe<ReaderSubscriptionMessage.EventReaderEof>(_eventReaderCoreService);
		coreInputBus.Subscribe<ReaderSubscriptionMessage.EventReaderPartitionEof>(_eventReaderCoreService);
		coreInputBus.Subscribe<ReaderSubscriptionMessage.EventReaderPartitionDeleted>(_eventReaderCoreService);
		coreInputBus.Subscribe<ReaderSubscriptionMessage.EventReaderNotAuthorized>(_eventReaderCoreService);
		coreInputBus.Subscribe<ReaderSubscriptionMessage.Faulted>(_eventReaderCoreService);
		coreInputBus.Subscribe<ReaderSubscriptionMessage.ReportProgress>(_eventReaderCoreService);
		//NOTE: message forwarding is set up outside (for Read/Write events)
	}
}
