// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Common.Options;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Management;
using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;
using static KurrentDB.Projections.Core.Messages.ProjectionCoreServiceMessage;
using static KurrentDB.Projections.Core.Messages.ProjectionSubsystemMessage;

namespace KurrentDB.Projections.Core.Tests.Services.core_coordinator;

[TestFixture]
public class when_restarting_with_projection_type_none {
	private FakePublisher[] queues;
	private FakePublisher publisher;
	private ProjectionCoreCoordinator _coordinator;
	private Guid instanceCorrelationId = Guid.NewGuid();
	private Guid queueId;

	[SetUp]
	public void Setup() {
		queues = [new()];
		publisher = new();
		_coordinator = new(ProjectionType.None, queues, publisher);

		// Start components
		_coordinator.Handle(new StartComponents(instanceCorrelationId));
		_coordinator.Handle(new SubComponentStarted(EventReaderCoreService.SubComponentName, instanceCorrelationId));

		// Stop components but don't handle component stopped
		_coordinator.Handle(new StopComponents(instanceCorrelationId));

		var stopReader = queues[0].Messages.OfType<ReaderCoreServiceMessage.StopReader>().First();
		queueId = stopReader.QueueId;
		//clear queues for clearer testing
		queues[0].Messages.Clear();
	}

	[Test]
	public void should_not_start_reader_if_subcomponents_not_stopped() {
		// None of the subcomponents stopped

		// Start components
		_coordinator.Handle(new StartComponents(Guid.NewGuid()));

		Assert.AreEqual(0, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StartReader).Count);
	}


	[Test]
	public void should_start_reader_if_subcomponents_stopped_before_starting_components_again() {
		// Component stopped
		_coordinator.Handle(new SubComponentStopped(EventReaderCoreService.SubComponentName, queueId));

		// Start component
		_coordinator.Handle(new StartComponents(Guid.NewGuid()));

		Assert.AreEqual(1, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StartReader).Count);
	}

	[Test]
	public void should_not_stop_reader_if_subcomponents_not_started_yet() {
		var newInstanceCorrelationId = Guid.NewGuid();

		// Stop components
		_coordinator.Handle(new SubComponentStopped(EventReaderCoreService.SubComponentName, queueId));

		// Start components but don't handle component started
		_coordinator.Handle(new StartComponents(newInstanceCorrelationId));

		// Stop components
		_coordinator.Handle(new StopComponents(newInstanceCorrelationId));

		Assert.AreEqual(0, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StopReader).Count);
	}

	[Test]
	public void should_not_stop_reader_if_subcomponents_not_started() {
		// Stop components
		_coordinator.Handle(new SubComponentStopped(EventReaderCoreService.SubComponentName, queueId));

		// Stop components without starting
		_coordinator.Handle(new StopComponents(Guid.NewGuid()));

		Assert.AreEqual(0, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StopReader).Count);
	}
}
