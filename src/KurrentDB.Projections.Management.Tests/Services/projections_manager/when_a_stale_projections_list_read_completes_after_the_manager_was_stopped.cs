// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Common.Options;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Messages;
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

[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class when_a_stale_projections_list_read_completes_after_the_manager_was_stopped<TLogFormat, TStreamId>
	: TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private ProjectionManager _manager;
	private Guid _workerId;
	private Guid _firstInstance;
	private ClientMessage.ReadStreamEventsForward _staleRead;
	private int _createdWhileStopped;

	protected override void Given() {
		_workerId = Guid.NewGuid();
		ExistingEvent(ProjectionNamesBuilder.ProjectionsRegistrationStream, ProjectionEventTypes.ProjectionCreated,
			null, "projection1");
		ExistingEvent(
			"$projections-projection1", ProjectionEventTypes.ProjectionUpdated, null,
			@"{""Query"":""fromAll(); on_any(function(){});"", ""Mode"":""3"", ""Enabled"":true, ""HandlerType"":""JS""}");
		// the first read of the registration stream is "in flight" (no reply) until we replay it
		TimeOutReadToStreamOnce(ProjectionNamesBuilder.ProjectionsRegistrationStream);
	}

	[SetUp]
	public void setup() {
		var queues = new Dictionary<Guid, IPublisher> { { _workerId, _bus } };
		_manager = new ProjectionManager(
			_bus,
			_bus,
			queues,
			new FakeTimeProvider(),
			ProjectionType.All,
			_ioDispatcher,
			TimeSpan.FromMinutes(Opts.ProjectionsQueryExpiryDefault),
			IProjectionTracker.NoOp);
		_bus.Subscribe<ClientMessage.WriteEventsCompleted>(_manager);
		_bus.Subscribe<ClientMessage.ReadStreamEventsBackwardCompleted>(_manager);
		_bus.Subscribe<ClientMessage.ReadStreamEventsForwardCompleted>(_manager);

		_firstInstance = Guid.NewGuid();
		_manager.Handle(new ProjectionSubsystemMessage.StartComponents(_firstInstance));
		_staleRead = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Single();
		_manager.Handle(new ProjectionSubsystemMessage.StopComponents(_firstInstance));

		// the read issued before Stop() is answered only now
		Handle(_staleRead);
		_createdWhileStopped = _consumer.HandledMessages
			.OfType<CoreProjectionManagementMessage.CreateAndPrepare>().Count();

		// the next leader election starts a new instance, which reads the registration stream again
		_manager.Handle(new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));
	}

	[TearDown]
	public void TearDown() {
		_manager.Dispose();
	}

	[Test]
	public void ignores_the_stale_read_while_stopped_and_loads_the_projections_on_the_next_start() {
		Assert.AreEqual(0, _createdWhileStopped, "a stale read must not create projections while the manager is stopped");

		_manager.Handle(new ProjectionManagementMessage.Command.GetStatistics(_bus, null, null));
		var names = _consumer.HandledMessages.OfType<ProjectionManagementMessage.Statistics>()
			.SelectMany(s => s.Projections)
			.Select(p => p.Name)
			.ToArray();
		CollectionAssert.Contains(names, "projection1");
		Assert.AreEqual(1, _consumer.HandledMessages
			.OfType<CoreProjectionManagementMessage.CreateAndPrepare>().Count(c => c.Name == "projection1"));
	}
}
