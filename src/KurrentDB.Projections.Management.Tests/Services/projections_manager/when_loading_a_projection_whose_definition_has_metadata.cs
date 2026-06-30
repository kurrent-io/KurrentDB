// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Common.Options;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.Services.TimeService;
using KurrentDB.Core.Util;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Metrics;
using KurrentDB.Projections.Core.Services;
using KurrentDB.Projections.Core.Services.Management;
using KurrentDB.Projections.Core.Services.Processing;
using KurrentDB.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.projections_manager;

// Forward-compat keystone: the load path reads only Event.Data, never Event.Metadata, so a definition event
// that carries caller metadata must load exactly as one without it.
[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class when_loading_a_projection_whose_definition_has_metadata<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private new ITimeProvider _timeProvider;
	private ProjectionManager _manager;
	private Guid _workerId;

	protected override void Given() {
		_workerId = Guid.NewGuid();
		ExistingEvent(ProjectionNamesBuilder.ProjectionsRegistrationStream, ProjectionEventTypes.ProjectionCreated,
			null, "projection1");
		ExistingEvent(
			"$projections-projection1", ProjectionEventTypes.ProjectionUpdated,
			@"{""deploy"":""abc123""}",
			@"{""Query"":""fromAll(); on_any(function(){});log('hello');"", ""Mode"":""3"", ""Enabled"":true, ""HandlerType"":""JS""}");
	}

	[SetUp]
	public void setup() {
		_timeProvider = new FakeTimeProvider();
		var queues = new Dictionary<Guid, IPublisher> { { _workerId, _bus } };
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
	public void the_projection_loads_normally() {
		_manager.Handle(new ProjectionManagementMessage.Command.GetStatistics(_bus, null, "projection1"));
		var stats = _consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
			.SingleOrDefault(v => v.Projections[0].Name == "projection1");
		Assert.IsNotNull(stats);
		Assert.AreEqual(ManagedProjectionState.Preparing, stats.Projections[0].LeaderStatus);
	}

	[Test]
	public void the_definition_body_is_read_intact() {
		_manager.Handle(
			new ProjectionManagementMessage.Command.GetQuery(
				_bus, "projection1", ProjectionManagementMessage.RunAs.Anonymous));
		var query = _consumer.HandledMessages.OfType<ProjectionManagementMessage.ProjectionQuery>().Single();
		Assert.AreEqual("projection1", query.Name);
		StringAssert.Contains("fromAll()", query.Query);
	}
}
