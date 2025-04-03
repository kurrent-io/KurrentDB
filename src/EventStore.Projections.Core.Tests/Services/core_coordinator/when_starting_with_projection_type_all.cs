// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using NUnit.Framework;
using EventStore.Projections.Core.Services.Management;
using EventStore.Common.Options;
using EventStore.Projections.Core.Messages;
using EventStore.Core.Tests.Fakes;
using System.Collections.Generic;

namespace EventStore.Projections.Core.Tests.Services.core_coordinator;

[TestFixture]
public class when_starting_with_projection_type_all {
	private FakePublisher[] queues;
	private FakePublisher publisher;
	private ProjectionCoreCoordinator _coordinator;

	[SetUp]
	public void Setup() {
		queues = new List<FakePublisher>() {new FakePublisher()}.ToArray();
		publisher = new FakePublisher();

		_coordinator =
			new ProjectionCoreCoordinator(ProjectionType.All, queues, publisher);
		_coordinator.Handle(new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));
	}

	[Test]
	public void should_publish_start_reader_messages() {
		Assert.AreEqual(1, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StartReader).Count);
	}

	[Test]
	public void should_publish_start_core_messages() {
		Assert.AreEqual(1,
			queues[0].Messages.FindAll(x => x.GetType() == typeof(ProjectionCoreServiceMessage.StartCore)).Count);
	}
}
