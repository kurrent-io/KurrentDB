// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using KurrentDB.Common.Options;
using KurrentDB.Core.Tests.Fakes;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Management;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.core_coordinator;

[TestFixture]
public class when_starting_with_projection_type_none {
	private FakePublisher[] queues;
	private FakePublisher publisher;
	private ProjectionCoreCoordinator _coordinator;

	[SetUp]
	public void Setup() {
		queues = [new()];
		publisher = new();
		_coordinator = new(ProjectionType.None, queues, publisher);
		_coordinator.Handle(new ProjectionSubsystemMessage.StartComponents(Guid.NewGuid()));
	}

	[Test]
	public void should_publish_start_reader_messages() {
		Assert.AreEqual(1, queues[0].Messages.FindAll(x => x is ReaderCoreServiceMessage.StartReader).Count);
	}

	[Test]
	public void should_not_publish_start_core_messages() {
		Assert.AreEqual(0, queues[0].Messages.FindAll(x => x.GetType() == typeof(ProjectionCoreServiceMessage.StartCore)).Count);
	}
}
