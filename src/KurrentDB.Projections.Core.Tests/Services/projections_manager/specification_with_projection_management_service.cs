// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Common.Options;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Helpers;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.AwakeReaderService;
using KurrentDB.Core.Tests.Helpers;
using KurrentDB.Core.Util;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Metrics;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Management;
using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;
using AwakeServiceMessage = KurrentDB.Core.Services.AwakeReaderService.AwakeServiceMessage;
using IODispatcherDelayedMessage = KurrentDB.Core.Helpers.IODispatcherDelayedMessage;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager;

public abstract class specification_with_projection_management_service<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private ProjectionManager _manager;
	private ProjectionManagerMessageDispatcher _managerMessageDispatcher;
	private bool _initializeSystemProjections;
	private AwakeService _awakeService;

	protected override void Given1() {
		base.Given1();
		_initializeSystemProjections = GivenInitializeSystemProjections();
		if (!_initializeSystemProjections) {
			ExistingEvent(ProjectionNamesBuilder.ProjectionsRegistrationStream, ProjectionEventTypes.ProjectionsInitialized, "", "");
		}
	}

	protected virtual bool GivenInitializeSystemProjections() => false;

	protected override ManualQueue GiveInputQueue() => new(_bus, _timeProvider);

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

		_awakeService = new AwakeService();
		_bus.Subscribe<StorageMessage.EventCommitted>(_awakeService);
		_bus.Subscribe<StorageMessage.TfEofAtNonCommitRecord>(_awakeService);
		_bus.Subscribe<AwakeServiceMessage.SubscribeAwake>(_awakeService);
		_bus.Subscribe<AwakeServiceMessage.UnsubscribeAwake>(_awakeService);

		Given();
		WhenLoop();
	}

	protected abstract Dictionary<Guid, IPublisher> GivenCoreQueues();
}
