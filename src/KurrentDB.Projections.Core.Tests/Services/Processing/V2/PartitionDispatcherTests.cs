// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Projections.Core.Services.Processing.V2;
using ResolvedEvent = KurrentDB.Projections.Core.Services.Processing.ResolvedEvent;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.Processing.V2;

[TestFixture]
public class PartitionDispatcherTests {
	private static ResolvedEvent CreateTestEvent(string streamId, long seqNo = 0, long position = 100) {
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

		// Find which partition got events
		int partitionWithEvents = -1;
		for (int i = 0; i < 4; i++) {
			var reader = dispatcher.GetPartitionReader(i);
			if (reader.TryRead(out _)) {
				partitionWithEvents = i;
				break;
			}
		}

		Assert.That(partitionWithEvents, Is.GreaterThanOrEqualTo(0), "At least one partition should have events");

		// Second event should be on the same partition
		var targetReader = dispatcher.GetPartitionReader(partitionWithEvents);
		Assert.That(targetReader.TryRead(out var pe), Is.True, "Second event should be on the same partition");
		Assert.That(pe.PartitionKey, Is.EqualTo("stream-1"));
	}

	[Test]
	public async Task events_with_different_keys_can_route_to_different_partitions() {
		// Use many partitions and many different keys to make collisions unlikely
		const int partitionCount = 16;
		var dispatcher = new PartitionDispatcher(
			partitionCount: partitionCount,
			getPartitionKey: e => e.EventStreamId);

		var streamNames = new List<string>();
		for (int i = 0; i < 100; i++) {
			streamNames.Add($"stream-{i}");
		}

		long pos = 100;
		foreach (var stream in streamNames) {
			var evt = CreateTestEvent(stream, 0, pos);
			await dispatcher.DispatchEvent(evt, new TFPos(pos, pos), CancellationToken.None);
			pos += 100;
		}

		dispatcher.Complete();

		// Count events per partition
		var partitionsWithEvents = new HashSet<int>();
		for (int i = 0; i < partitionCount; i++) {
			var reader = dispatcher.GetPartitionReader(i);
			if (reader.TryRead(out _)) {
				partitionsWithEvents.Add(i);
			}
		}

		// With 100 different keys across 16 partitions, more than 1 partition should have events
		Assert.That(partitionsWithEvents.Count, Is.GreaterThan(1),
			"Events with different keys should distribute across multiple partitions");
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
			Assert.That(reader.TryRead(out var pe), Is.True,
				$"Partition {i} should have received the checkpoint marker");
			Assert.That(pe.IsCheckpointMarker, Is.True,
				$"Event on partition {i} should be a checkpoint marker");
			Assert.That(pe.LogPosition, Is.EqualTo(logPosition));
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

		Assert.That(seq1, Is.EqualTo(1UL));
		Assert.That(seq2, Is.EqualTo(2UL));
		Assert.That(seq3, Is.EqualTo(3UL));

		// Also verify the sequence numbers are on the partition events
		var reader = dispatcher.GetPartitionReader(0);
		reader.TryRead(out var pe1);
		reader.TryRead(out var pe2);
		reader.TryRead(out var pe3);

		Assert.That(pe1.CheckpointMarkerSequence, Is.EqualTo(1UL));
		Assert.That(pe2.CheckpointMarkerSequence, Is.EqualTo(2UL));
		Assert.That(pe3.CheckpointMarkerSequence, Is.EqualTo(3UL));
	}

	[Test]
	public async Task complete_finishes_all_channels() {
		const int partitionCount = 4;
		var dispatcher = new PartitionDispatcher(
			partitionCount: partitionCount,
			getPartitionKey: e => e.EventStreamId);

		// Write one event so we know the dispatcher is active
		var evt = CreateTestEvent("stream-1");
		await dispatcher.DispatchEvent(evt, new TFPos(100, 100), CancellationToken.None);

		dispatcher.Complete();

		for (int i = 0; i < partitionCount; i++) {
			var reader = dispatcher.GetPartitionReader(i);
			// Drain any existing events
			while (reader.TryRead(out _)) { }

			// After completion and draining, TryRead should return false and Completion should be done
			Assert.That(reader.TryRead(out _), Is.False,
				$"Partition {i} should have no more events after completion");
			Assert.That(reader.Completion.IsCompleted, Is.True,
				$"Partition {i} channel should be completed");
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

		Assert.That(count, Is.EqualTo(3), "All events should route to partition 0 with a single partition");
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
		Assert.That(reader.TryRead(out var pe), Is.True);
		Assert.That(pe.PartitionKey, Is.EqualTo("my-stream"));
		Assert.That(pe.LogPosition, Is.EqualTo(logPos));
		Assert.That(pe.IsCheckpointMarker, Is.False);
		Assert.That(pe.Event, Is.Not.Null);
	}
}
