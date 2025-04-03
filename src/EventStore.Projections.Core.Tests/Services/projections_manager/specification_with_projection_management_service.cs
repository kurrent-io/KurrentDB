// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Common.Options;
using EventStore.Core.Bus;
using EventStore.Core.Helpers;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.AwakeReaderService;
using EventStore.Core.Tests.Helpers;
using EventStore.Core.Util;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Metrics;
using EventStore.Projections.Core.Services.Management;
using NUnit.Framework;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services;

namespace EventStore.Projections.Core.Tests.Services.projections_manager;

public abstract class specification_with_projection_management_service<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	protected ProjectionManager _manager;
	protected ProjectionManagerMessageDispatcher _managerMessageDispatcher;
	private bool _initializeSystemProjections;
	protected AwakeService AwakeService;


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

		var queues = GivenCoreQueues();
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

		IPublisher inputQueue = GetInputQueue();
		IPublisher publisher = GetInputQueue();
		var ioDispatcher = new IODispatcher(publisher, inputQueue, true);
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
		_bus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(_manager);
		_bus.Subscribe<ClientMessage.WriteEventsCompleted>(_manager);
		_bus.Subscribe<ProjectionSubsystemMessage.StartComponents>(_manager);
		_bus.Subscribe<ProjectionSubsystemMessage.StopComponents>(_manager);
		_bus.Subscribe<CoreProjectionManagementControlMessage>(_managerMessageDispatcher);

		_bus.Subscribe<ClientMessage.ReadStreamEventsForwardCompleted>(ioDispatcher.ForwardReader);
		_bus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(ioDispatcher.BackwardReader);
		_bus.Subscribe<ClientMessage.NotHandled>(ioDispatcher.BackwardReader);
		_bus.Subscribe<ClientMessage.WriteEventsCompleted>(ioDispatcher.Writer);
		_bus.Subscribe<ClientMessage.DeleteStreamCompleted>(ioDispatcher.StreamDeleter);
		_bus.Subscribe<IODispatcherDelayedMessage>(ioDispatcher.Awaker);
		_bus.Subscribe<IODispatcherDelayedMessage>(ioDispatcher);

		AwakeService = new AwakeService();
		_bus.Subscribe<StorageMessage.EventCommitted>(AwakeService);
		_bus.Subscribe<StorageMessage.TfEofAtNonCommitRecord>(AwakeService);
		_bus.Subscribe<AwakeServiceMessage.SubscribeAwake>(AwakeService);
		_bus.Subscribe<AwakeServiceMessage.UnsubscribeAwake>(AwakeService);


		Given();
		WhenLoop();
	}

	protected abstract Dictionary<Guid, IPublisher> GivenCoreQueues();
}
