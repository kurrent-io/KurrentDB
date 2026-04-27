// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Emitting.EmittedEvents;
using KurrentDB.Projections.Core.Services.Processing.V2;

namespace KurrentDB.Projections.V2.Tests.Unit;

public class OutputBufferTests {
	static CheckpointTag TestCheckpointTag =>
		CheckpointTag.FromPosition(0, 100, 100);

	static EmittedEventEnvelope CreateTestEmittedEvent(string streamId, string eventType) {
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
	public async Task clear_resets_all_state() {
		var buffer = new OutputBuffer();

		buffer.AddEmittedEvents([CreateTestEmittedEvent("stream-1", "EventA")]);
		buffer.SetPartitionState("partition-1", "state-stream-1", "{\"count\":1}", 0);
		buffer.LastLogPosition = new TFPos(500, 500);

		buffer.Clear();

		await Assert.That(buffer.EmittedEvents).IsEmpty();
		await Assert.That(buffer.DirtyStates).IsEmpty();
		await Assert.That(buffer.LastLogPosition).IsEqualTo(default(TFPos));
	}

	[Test]
	public async Task set_partition_state_overwrites_previous() {
		var buffer = new OutputBuffer();

		buffer.SetPartitionState("partition-1", "state-stream-1", "{\"count\":1}", 0);
		buffer.SetPartitionState("partition-1", "state-stream-1", "{\"count\":2}", 1);

		await Assert.That(buffer.DirtyStates).HasCount(1);

		var (streamName, stateJson, expectedVersion) = buffer.DirtyStates["partition-1"];
		await Assert.That(streamName).IsEqualTo("state-stream-1");
		await Assert.That(stateJson).IsEqualTo("{\"count\":2}");
		await Assert.That(expectedVersion).IsEqualTo(1);
	}

	[Test]
	public async Task add_emitted_events_handles_null() {
		var buffer = new OutputBuffer();

		buffer.AddEmittedEvents(null);

		await Assert.That(buffer.EmittedEvents).IsEmpty();
	}

	[Test]
	public async Task add_emitted_events_handles_empty_array() {
		var buffer = new OutputBuffer();

		buffer.AddEmittedEvents(Array.Empty<EmittedEventEnvelope>());

		await Assert.That(buffer.EmittedEvents).IsEmpty();
	}

	[Test]
	public async Task add_emitted_events_accumulates() {
		var buffer = new OutputBuffer();

		buffer.AddEmittedEvents([CreateTestEmittedEvent("stream-1", "EventA")]);
		buffer.AddEmittedEvents([
			CreateTestEmittedEvent("stream-2", "EventB"),
			CreateTestEmittedEvent("stream-3", "EventC")
		]);

		await Assert.That(buffer.EmittedEvents).HasCount(3);
	}

	[Test]
	public async Task set_partition_state_stores_multiple_partitions() {
		var buffer = new OutputBuffer();

		buffer.SetPartitionState("partition-1", "state-stream-1", "{\"a\":1}", 0);
		buffer.SetPartitionState("partition-2", "state-stream-2", "{\"b\":2}", 0);
		buffer.SetPartitionState("partition-3", "state-stream-3", "{\"c\":3}", 0);

		await Assert.That(buffer.DirtyStates).HasCount(3);
		await Assert.That(buffer.DirtyStates.ContainsKey("partition-1")).IsTrue();
		await Assert.That(buffer.DirtyStates.ContainsKey("partition-2")).IsTrue();
		await Assert.That(buffer.DirtyStates.ContainsKey("partition-3")).IsTrue();
	}

	[Test]
	public async Task last_log_position_defaults_to_zero() {
		var buffer = new OutputBuffer();

		await Assert.That(buffer.LastLogPosition).IsEqualTo(default(TFPos));
	}
}
