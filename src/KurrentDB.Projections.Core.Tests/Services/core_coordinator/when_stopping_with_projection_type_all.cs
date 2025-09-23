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

namespace KurrentDB.Projections.Core.Tests.Services.core_coordinator;

[TestFixture]
public class when_stopping_with_projection_type_all {
	private FakePublisher[] queues;
	private FakePublisher publisher;
	private ProjectionCoreCoordinator _coordinator;
	private List<StopCore> _stopCoreMessages = [];

	[SetUp]
	public void Setup() {
		queues = [new()];
		publisher = new();
		var instanceCorrelationId = Guid.NewGuid();
		_coordinator = new(ProjectionType.All, queues, publisher);

		// Start all sub components
		_coordinator.Handle(new ProjectionSubsystemMessage.StartComponents(instanceCorrelationId));

		// All components started
		_coordinator.Handle(new SubComponentStarted(EventReaderCoreService.SubComponentName, instanceCorrelationId));
		_coordinator.Handle(new SubComponentStarted(ProjectionCoreService.SubComponentName, instanceCorrelationId));

		// Stop Components
		_coordinator.Handle(new ProjectionSubsystemMessage.StopComponents(instanceCorrelationId));

		// Publish SubComponent stopped messages for the projection core service
		_stopCoreMessages = queues[0].Messages
			.FindAll(x => x.GetType() == typeof(StopCore))
			.Select(x => x as StopCore)
			.ToList();
		foreach (var msg in _stopCoreMessages)
			_coordinator.Handle(new SubComponentStopped(ProjectionCoreService.SubComponentName, msg.QueueId));
	}

	[Test]
	public void should_publish_stop_core_messages() {
		Assert.AreEqual(1, _stopCoreMessages.Count);
	}

	[Test]
	public void should_publish_stop_reader_messages_after_core_stopped() {
		Assert.AreEqual(1, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StopReader).Count);
	}
}
