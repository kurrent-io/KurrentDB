// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services.Processing.V2;
using ResolvedEvent = KurrentDB.Projections.Core.Services.Processing.ResolvedEvent;

namespace KurrentDB.Projections.V2.Tests.Unit;

public class PartitionDispatcherTests {
	static ResolvedEvent CreateTestEvent(string streamId, long seqNo = 0, long position = 100) {
		return new ResolvedEvent(
			positionStreamId: streamId,
			positionSequenceNumber: seqNo,
			eventStreamId: streamId,
			eventSequenceNumber: seqNo,
			resolvedLinkTo: false,
			position: new TFPos(position, position),
			eventId: Guid.NewGuid(),
			eventType: "TestEvent",
			isJson: true,
			data: "{}",
			metadata: null);
	}

	[Test]
	public async Task events_with_same_key_route_to_same_partition() {
		var dispatcher = new PartitionDispatcher(
			partitionCount: 4,
			getPartitionKey: e => e.EventStreamId);

		var event1 = CreateTestEvent("stream-1", 0, 100);
		var event2 = CreateTestEvent("stream-1", 1, 200);

		await dispatcher.DispatchEvent(event1, new TFPos(100, 100), CancellationToken.None);
		await dispatcher.DispatchEvent(event2, new TFPos(200, 200), CancellationToken.None);
		dispatcher.Complete();

		int partitionWithEvents = -1;
		for (int i = 0; i < 4; i++) {
			var reader = dispatcher.GetPartitionReader(i);
			if (reader.TryRead(out _)) {
				partitionWithEvents = i;
				break;
			}
		}

		await Assert.That(partitionWithEvents).IsGreaterThanOrEqualTo(0);

		var targetReader = dispatcher.GetPartitionReader(partitionWithEvents);
		await Assert.That(targetReader.TryRead(out var pe)).IsTrue();
		await Assert.That(pe.PartitionKey).IsEqualTo("stream-1");
	}

	[Test]
	public async Task events_with_different_keys_can_route_to_different_partitions() {
		const int partitionCount = 16;
		var dispatcher = new PartitionDispatcher(
			partitionCount: partitionCount,
			getPartitionKey: e => e.EventStreamId);

		long pos = 100;
		for (int i = 0; i < 100; i++) {
			var evt = CreateTestEvent($"stream-{i}", 0, pos);
			await dispatcher.DispatchEvent(evt, new TFPos(pos, pos), CancellationToken.None);
			pos += 100;
		}

		dispatcher.Complete();

		var partitionsWithEvents = new HashSet<int>();
		for (int i = 0; i < partitionCount; i++) {
			var reader = dispatcher.GetPartitionReader(i);
			if (reader.TryRead(out _))
				partitionsWithEvents.Add(i);
		}

		await Assert.That(partitionsWithEvents.Count).IsGreaterThan(1);
	}

	[Test]
	public async Task checkpoint_marker_sent_to_all_partitions() {
		const int partitionCount = 4;
		var dispatcher = new PartitionDispatcher(
			partitionCount: partitionCount,
			getPartitionKey: e => e.EventStreamId);

		var logPosition = new TFPos(500, 500);
		await dispatcher.InjectCheckpointMarker(logPosition, CancellationToken.None);
		dispatcher.Complete();

		for (int i = 0; i < partitionCount; i++) {
			var reader = dispatcher.GetPartitionReader(i);
			await Assert.That(reader.TryRead(out var pe)).IsTrue();
			await Assert.That(pe.IsCheckpointMarker).IsTrue();
			await Assert.That(pe.LogPosition).IsEqualTo(logPosition);
		}
	}

	[Test]
	public async Task checkpoint_markers_have_incrementing_sequence() {
		var dispatcher = new PartitionDispatcher(
			partitionCount: 1,
			getPartitionKey: e => e.EventStreamId);

		var seq1 = await dispatcher.InjectCheckpointMarker(new TFPos(100, 100), CancellationToken.None);
		var seq2 = await dispatcher.InjectCheckpointMarker(new TFPos(200, 200), CancellationToken.None);
		var seq3 = await dispatcher.InjectCheckpointMarker(new TFPos(300, 300), CancellationToken.None);
		dispatcher.Complete();

		await Assert.That(seq1).IsEqualTo(1UL);
		await Assert.That(seq2).IsEqualTo(2UL);
		await Assert.That(seq3).IsEqualTo(3UL);

		var reader = dispatcher.GetPartitionReader(0);
		reader.TryRead(out var pe1);
		reader.TryRead(out var pe2);
		reader.TryRead(out var pe3);

		await Assert.That(pe1.CheckpointMarkerSequence).IsEqualTo(1UL);
		await Assert.That(pe2.CheckpointMarkerSequence).IsEqualTo(2UL);
		await Assert.That(pe3.CheckpointMarkerSequence).IsEqualTo(3UL);
	}

	[Test]
	public async Task complete_finishes_all_channels() {
		const int partitionCount = 4;
		var dispatcher = new PartitionDispatcher(
			partitionCount: partitionCount,
			getPartitionKey: e => e.EventStreamId);

		var evt = CreateTestEvent("stream-1");
		await dispatcher.DispatchEvent(evt, new TFPos(100, 100), CancellationToken.None);

		dispatcher.Complete();

		for (int i = 0; i < partitionCount; i++) {
			var reader = dispatcher.GetPartitionReader(i);
			while (reader.TryRead(out _)) { }

			await Assert.That(reader.TryRead(out _)).IsFalse();
			await Assert.That(reader.Completion.IsCompleted).IsTrue();
		}
	}

	[Test]
	public async Task single_partition_routes_all_events_to_partition_zero() {
		var dispatcher = new PartitionDispatcher(
			partitionCount: 1,
			getPartitionKey: e => e.EventStreamId);

		var event1 = CreateTestEvent("stream-A", 0, 100);
		var event2 = CreateTestEvent("stream-B", 0, 200);
		var event3 = CreateTestEvent("stream-C", 0, 300);

		await dispatcher.DispatchEvent(event1, new TFPos(100, 100), CancellationToken.None);
		await dispatcher.DispatchEvent(event2, new TFPos(200, 200), CancellationToken.None);
		await dispatcher.DispatchEvent(event3, new TFPos(300, 300), CancellationToken.None);
		dispatcher.Complete();

		var reader = dispatcher.GetPartitionReader(0);
		int count = 0;
		while (reader.TryRead(out _)) count++;

		await Assert.That(count).IsEqualTo(3);
	}

	[Test]
	public async Task dispatched_event_preserves_partition_key_and_position() {
		var dispatcher = new PartitionDispatcher(
			partitionCount: 1,
			getPartitionKey: e => e.EventStreamId);

		var evt = CreateTestEvent("my-stream", 5, 42);
		var logPos = new TFPos(42, 42);

		await dispatcher.DispatchEvent(evt, logPos, CancellationToken.None);
		dispatcher.Complete();

		var reader = dispatcher.GetPartitionReader(0);
		await Assert.That(reader.TryRead(out var pe)).IsTrue();
		await Assert.That(pe.PartitionKey).IsEqualTo("my-stream");
		await Assert.That(pe.LogPosition).IsEqualTo(logPos);
		await Assert.That(pe.IsCheckpointMarker).IsFalse();
		await Assert.That(pe.Event).IsNotNull();
	}
}
