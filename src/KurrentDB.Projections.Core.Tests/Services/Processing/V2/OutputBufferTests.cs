// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using KurrentDB.Projections.Core.Services.Processing.V2;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.Processing.V2;

[TestFixture]
public class OutputBufferTests {
	private static CheckpointTag TestCheckpointTag =>
		CheckpointTag.FromPosition(0, 100, 100);

	private static EmittedEventEnvelope CreateTestEmittedEvent(string streamId, string eventType) {
		var emittedEvent = new EmittedDataEvent(
			streamId: streamId,
			eventId: Guid.NewGuid(),
			eventType: eventType,
			isJson: true,
			data: "{}",
			metadata: null,
			causedByTag: TestCheckpointTag,
			expectedTag: null);
		return new EmittedEventEnvelope(emittedEvent);
	}

	[Test]
	public void clear_resets_all_state() {
		var buffer = new OutputBuffer();

		// Populate all fields
		buffer.AddEmittedEvents(new[] { CreateTestEmittedEvent("stream-1", "EventA") });
		buffer.SetPartitionState("partition-1", "state-stream-1", "{\"count\":1}", 0);
		buffer.LastLogPosition = new TFPos(500, 500);

		// Verify populated
		Assert.That(buffer.EmittedEvents.Count, Is.EqualTo(1));
		Assert.That(buffer.DirtyStates.Count, Is.EqualTo(1));
		Assert.That(buffer.LastLogPosition, Is.Not.EqualTo(default(TFPos)));

		// Clear and verify
		buffer.Clear();

		Assert.That(buffer.EmittedEvents, Is.Empty);
		Assert.That(buffer.DirtyStates, Is.Empty);
		Assert.That(buffer.LastLogPosition, Is.EqualTo(default(TFPos)));
	}

	[Test]
	public void set_partition_state_overwrites_previous() {
		var buffer = new OutputBuffer();

		buffer.SetPartitionState("partition-1", "state-stream-1", "{\"count\":1}", 0);
		buffer.SetPartitionState("partition-1", "state-stream-1", "{\"count\":2}", 1);

		Assert.That(buffer.DirtyStates.Count, Is.EqualTo(1));

		var (streamName, stateJson, expectedVersion) = buffer.DirtyStates["partition-1"];
		Assert.That(streamName, Is.EqualTo("state-stream-1"));
		Assert.That(stateJson, Is.EqualTo("{\"count\":2}"));
		Assert.That(expectedVersion, Is.EqualTo(1));
	}

	[Test]
	public void add_emitted_events_handles_null() {
		var buffer = new OutputBuffer();

		buffer.AddEmittedEvents(null);

		Assert.That(buffer.EmittedEvents, Is.Empty);
	}

	[Test]
	public void add_emitted_events_handles_empty_array() {
		var buffer = new OutputBuffer();

		buffer.AddEmittedEvents(Array.Empty<EmittedEventEnvelope>());

		Assert.That(buffer.EmittedEvents, Is.Empty);
	}

	[Test]
	public void add_emitted_events_accumulates() {
		var buffer = new OutputBuffer();

		buffer.AddEmittedEvents(new[] { CreateTestEmittedEvent("stream-1", "EventA") });
		buffer.AddEmittedEvents(new[] {
			CreateTestEmittedEvent("stream-2", "EventB"),
			CreateTestEmittedEvent("stream-3", "EventC")
		});

		Assert.That(buffer.EmittedEvents.Count, Is.EqualTo(3));
	}

	[Test]
	public void set_partition_state_stores_multiple_partitions() {
		var buffer = new OutputBuffer();

		buffer.SetPartitionState("partition-1", "state-stream-1", "{\"a\":1}", 0);
		buffer.SetPartitionState("partition-2", "state-stream-2", "{\"b\":2}", 0);
		buffer.SetPartitionState("partition-3", "state-stream-3", "{\"c\":3}", 0);

		Assert.That(buffer.DirtyStates.Count, Is.EqualTo(3));
		Assert.That(buffer.DirtyStates.ContainsKey("partition-1"), Is.True);
		Assert.That(buffer.DirtyStates.ContainsKey("partition-2"), Is.True);
		Assert.That(buffer.DirtyStates.ContainsKey("partition-3"), Is.True);
	}

	[Test]
	public void last_log_position_defaults_to_zero() {
		var buffer = new OutputBuffer();

		Assert.That(buffer.LastLogPosition, Is.EqualTo(default(TFPos)));
	}
}
