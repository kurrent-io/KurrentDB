// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using EventStore.Projections.Core.Services.Processing.Partitioning;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.partition_state_update_manager;

[TestFixture]
public class when_created {
	private PartitionStateUpdateManager _updateManager;

	[SetUp]
	public void setup() {
		_updateManager = new PartitionStateUpdateManager(ProjectionNamesBuilder.CreateForTest("projection"));
	}

	[Test]
	public void handles_state_updated() {
		_updateManager.StateUpdated("partition",
			new PartitionState("state", null, CheckpointTag.FromPosition(0, 100, 50)),
			CheckpointTag.FromPosition(0, 200, 150));
	}

	[Test]
	public void emit_events_does_not_write_any_events() {
		_updateManager.EmitEvents(new FakeEventWriter());
	}

	class FakeEventWriter : IEventWriter {
		public void ValidateOrderAndEmitEvents(EmittedEventEnvelope[] events) {
			Assert.Fail("Should not write any events");
		}
	}
}
