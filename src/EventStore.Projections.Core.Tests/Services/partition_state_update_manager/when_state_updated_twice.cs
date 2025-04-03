// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.Checkpointing;
using EventStore.Projections.Core.Services.Processing.Emitting;
using EventStore.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using EventStore.Projections.Core.Services.Processing.Partitioning;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.partition_state_update_manager;

[TestFixture]
public class when_state_updated_twice {
	private PartitionStateUpdateManager _updateManager;
	private CheckpointTag _zero = CheckpointTag.FromPosition(0, 100, 50);
	private CheckpointTag _one = CheckpointTag.FromPosition(0, 200, 150);
	private CheckpointTag _two = CheckpointTag.FromPosition(0, 300, 250);
	private CheckpointTag _three = CheckpointTag.FromPosition(0, 400, 350);

	[SetUp]
	public void setup() {
		_updateManager = new PartitionStateUpdateManager(ProjectionNamesBuilder.CreateForTest("projection"));
		_updateManager.StateUpdated("partition", new PartitionState("{\"state\":1}", null, _one), _zero);
		_updateManager.StateUpdated("partition", new PartitionState("{\"state\":2}", null, _two), _one);
	}

	[Test]
	public void handles_state_updated_for_the_same_partition() {
		_updateManager.StateUpdated("partition", new PartitionState("{\"state\":1}", null, _three), _two);
	}

	[Test]
	public void handles_state_updated_for_another_partition() {
		_updateManager.StateUpdated("partition", new PartitionState("{\"state\":1}", null, _three), _two);
	}

	[Test]
	public void emit_events_writes_single_state_updated_event() {
		var eventWriter = new FakeEventWriter();
		_updateManager.EmitEvents(eventWriter);
		Assert.AreEqual(1, eventWriter.Writes.Count);
		Assert.AreEqual(1, eventWriter.Writes[0].Length);
	}

	[Test]
	public void emit_events_writes_correct_state_data() {
		var eventWriter = new FakeEventWriter();
		_updateManager.EmitEvents(eventWriter);
		EmittedEvent @event = eventWriter.Writes[0][0];
		Assert.AreEqual("[{\"state\":2}]", @event.Data);
	}

	[Test]
	public void emit_events_writes_event_with_correct_caused_by_tag() {
		var eventWriter = new FakeEventWriter();
		_updateManager.EmitEvents(eventWriter);
		EmittedEvent @event = eventWriter.Writes[0][0];
		Assert.AreEqual(_two, @event.CausedByTag);
	}

	[Test]
	public void emit_events_writes_event_with_correct_expected_tag() {
		var eventWriter = new FakeEventWriter();
		_updateManager.EmitEvents(eventWriter);
		EmittedEvent @event = eventWriter.Writes[0][0];
		Assert.AreEqual(_zero, @event.ExpectedTag);
	}
}
