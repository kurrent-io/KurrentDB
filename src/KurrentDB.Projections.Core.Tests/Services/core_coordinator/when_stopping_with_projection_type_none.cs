// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Common.Options;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Management;
using KurrentDB.Projections.Core.Services.Processing;
using NUnit.Framework;
using static KurrentDB.Projections.Core.Messages.ProjectionCoreServiceMessage;

namespace KurrentDB.Projections.Core.Tests.Services.core_coordinator;

[TestFixture]
public class when_stopping_with_projection_type_none {
	private FakePublisher[] queues;
	private FakePublisher publisher;
	private ProjectionCoreCoordinator _coordinator;

	[SetUp]
	public void Setup() {
		queues = [new()];
		publisher = new();
		var instanceCorrelationId = Guid.NewGuid();
		_coordinator = new(ProjectionType.None, queues, publisher);

		// Start components
		_coordinator.Handle(new ProjectionSubsystemMessage.StartComponents(instanceCorrelationId));

		// start sub components
		_coordinator.Handle(new SubComponentStarted(EventReaderCoreService.SubComponentName, instanceCorrelationId));

		//force stop
		_coordinator.Handle(new ProjectionSubsystemMessage.StopComponents(instanceCorrelationId));
	}

	[Test]
	public void should_publish_stop_reader_messages() {
		Assert.AreEqual(1, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StopReader).Count);
	}

	[Test]
	public void should_not_publish_stop_core_messages() {
		Assert.AreEqual(0, queues[0].Messages.FindAll(x => x.GetType() == typeof(StopCore)).Count);
	}
}
