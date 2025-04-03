// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Common.Options;
using EventStore.Core.Bus;
using EventStore.Core.Messages;
using EventStore.Core.Messaging;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests;
using EventStore.Core.Tests.Services.TimeService;
using EventStore.Core.Util;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Metrics;
using EventStore.Projections.Core.Services.Management;
using EventStore.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services;

namespace EventStore.Projections.Core.Tests.Services.projections_manager;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_starting_the_projection_manager_with_existing_partially_created_projection<TLogFormat, TStreamId> :
	TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private ProjectionManager _manager;
	private new ITimeProvider _timeProvider;
	private Guid _workerId;

	protected override void Given() {
		_workerId = Guid.NewGuid();
		ExistingEvent(ProjectionNamesBuilder.ProjectionsRegistrationStream, ProjectionEventTypes.ProjectionCreated,
			null, "projection1");
		NoStream("$projections-projection1");
	}


	[SetUp]
	public void setup() {
		_timeProvider = new FakeTimeProvider();
		var queues = new Dictionary<Guid, IPublisher> {{_workerId, _bus}};
		_manager = new ProjectionManager(
			_bus,
			_bus,
			queues,
			_timeProvider,
			ProjectionType.All,
			_ioDispatcher,
			TimeSpan.FromMinutes(Opts.ProjectionsQueryExpiryDefault),
			IProjectionTracker.NoOp);
		_bus.Subscribe<ClientMessage.WriteEventsCompleted>(_manager);
		_bus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(_manager);
		_bus.Subscribe<ClientMessage.ReadStreamEventsForwardCompleted>(_manager);
		_manager.Handle(new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));
	}

	[TearDown]
	public void TearDown() {
		_manager.Dispose();
	}

	[Test]
	public void projection_status_can_be_retrieved() {
		_manager.Handle(
			new ProjectionManagementMessage.Command.GetStatistics(_bus, null, "projection1",
				true));
		Assert.IsNotNull(
			_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().SingleOrDefault(
				v => v.Projections[0].Name == "projection1"));
	}

	[Test]
	public void projection_status_is_creating() {
		_manager.Handle(
			new ProjectionManagementMessage.Command.GetStatistics(_bus, null, "projection1",
				true));
		Assert.AreEqual(
			ManagedProjectionState.Creating,
			_consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>().SingleOrDefault(
				v => v.Projections[0].Name == "projection1").Projections[0].LeaderStatus);
	}
}
