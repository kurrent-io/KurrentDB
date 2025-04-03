// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using NUnit.Framework;
using EventStore.Projections.Core.Services.Management;
using EventStore.Common.Options;
using EventStore.Projections.Core.Messages;
using EventStore.Core.Tests.Fakes;
using System.Collections.Generic;
using EventStore.Projections.Core.Services.Processing;

namespace EventStore.Projections.Core.Tests.Services.core_coordinator;

[TestFixture]
public class when_stopping_with_projection_type_system {
	private FakePublisher[] queues;
	private FakePublisher publisher;
	private ProjectionCoreCoordinator _coordinator;

	private List<ProjectionCoreServiceMessage.StopCore> stopCoreMessages =
		new List<ProjectionCoreServiceMessage.StopCore>();

	[SetUp]
	public void Setup() {
		queues = new List<FakePublisher>() {new FakePublisher()}.ToArray();
		publisher = new FakePublisher();

		var instanceCorrelationId = Guid.NewGuid();
		_coordinator =
			new ProjectionCoreCoordinator(ProjectionType.System, queues, publisher);

		// Start components
		_coordinator.Handle(new ProjectionSubsystemMessage.StartComponents(instanceCorrelationId));

		// Start all sub components
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStarted(EventReaderCoreService.SubComponentName,
				instanceCorrelationId));
		_coordinator.Handle(
			new ProjectionCoreServiceMessage.SubComponentStarted(ProjectionCoreService.SubComponentName,
				instanceCorrelationId));

		// Stop components
		_coordinator.Handle(new ProjectionSubsystemMessage.StopComponents(instanceCorrelationId));

		// Publish SubComponent stopped messages for the projection core service
		stopCoreMessages = queues[0].Messages.OfType<ProjectionCoreServiceMessage.StopCore>().ToList();
		foreach (var msg in stopCoreMessages)
			_coordinator.Handle(
				new ProjectionCoreServiceMessage.SubComponentStopped(ProjectionCoreService.SubComponentName,
					msg.QueueId));
	}

	[Test]
	public void should_publish_stop_core_messages() {
		Assert.AreEqual(1, stopCoreMessages.Count);
	}

	[Test]
	public void should_publish_stop_reader_messages() {
		Assert.AreEqual(1, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StopReader).Count);
	}
}
