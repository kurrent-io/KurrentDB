// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using NUnit.Framework;
using EventStore.Projections.Core.Services.Management;
using EventStore.Common.Options;
using EventStore.Projections.Core.Messages;
using EventStore.Core.Tests.Fakes;
using System.Collections.Generic;
using System.Linq;
using EventStore.Projections.Core.Services.Processing;

namespace EventStore.Projections.Core.Tests.Services.core_coordinator;

[TestFixture]
public class when_restarting_with_projection_type_none {
	private FakePublisher[] queues;
	private FakePublisher publisher;
	private ProjectionCoreCoordinator _coordinator;
	private Guid instanceCorrelationId = Guid.NewGuid();
	private Guid queueId;

	[SetUp]
	public void Setup() {
		queues = new List<FakePublisher>() {new FakePublisher()}.ToArray();
		publisher = new FakePublisher();

		_coordinator =
			new ProjectionCoreCoordinator(ProjectionType.None, queues, publisher);

		// Start components
		_coordinator.Handle(new ProjectionSubsystemMessage.StartComponents(instanceCorrelationId));
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStarted(EventReaderCoreService.SubComponentName,
				instanceCorrelationId));

		// Stop components but don't handle component stopped
		_coordinator.Handle(new ProjectionSubsystemMessage.StopComponents(instanceCorrelationId));

		var stopReader = queues[0].Messages.OfType<ReaderCoreServiceMessage.StopReader>().First();
		queueId = stopReader.QueueId;
		//clear queues for clearer testing
		queues[0].Messages.Clear();
	}

	[Test]
	public void should_not_start_reader_if_subcomponents_not_stopped() {
		// None of the subcomponents stopped

		// Start components
		_coordinator.Handle(new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));

		Assert.AreEqual(0, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StartReader).Count);
	}


	[Test]
	public void should_start_reader_if_subcomponents_stopped_before_starting_components_again() {
		// Component stopped
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStopped(EventReaderCoreService.SubComponentName,
				queueId));

		// Start component
		_coordinator.Handle(new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));

		Assert.AreEqual(1, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StartReader).Count);
	}

	[Test]
	public void should_not_stop_reader_if_subcomponents_not_started_yet() {
		var newInstanceCorrelationId = Guid.NewGuid();

		// Stop components
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStopped(EventReaderCoreService.SubComponentName,
				queueId));

		// Start components but don't handle component started
		_coordinator.Handle(new ProjectionSubsystemMessage.StartComponents(newInstanceCorrelationId));

		// Stop components
		_coordinator.Handle(new ProjectionSubsystemMessage.StopComponents(newInstanceCorrelationId));

		Assert.AreEqual(0, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StopReader).Count);
	}

	[Test]
	public void should_not_stop_reader_if_subcomponents_not_started() {
		// Stop components
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStopped(EventReaderCoreService.SubComponentName,
				queueId));

		// Stop components without starting
		_coordinator.Handle(new ProjectionSubsystemMessage.StopComponents(Guid.NewGuid()));

		Assert.AreEqual(0, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StopReader).Count);
	}
}
