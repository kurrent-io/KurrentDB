// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Tests.Index.Hashers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.ReadIndex;

[TestFixture(3)]
[TestFixture(33)]
[TestFixture(123)]
[TestFixture(523)]
public class ReadEventInfoForward_KnownCollisions_Randomized : ReadIndexTestScenario<LogFormat.V2, string> {
	private const string Stream = "ab-1";
	private const string CollidingStream = "cb-1";

	private readonly Random _random = new Random();
	private readonly int _numEvents;
	private readonly List<EventRecord> _events;

	public ReadEventInfoForward_KnownCollisions_Randomized(int maxEntriesInMemTable) : base(
		chunkSize: 1_000_000,
		maxEntriesInMemTable: maxEntriesInMemTable,
		lowHasher: new ConstantHasher(0),
		highHasher: new HumanReadableHasher32()) {
		_numEvents = _random.Next(100, 400);
		_events = new List<EventRecord>(_numEvents);
	}

	private static void CheckResult(EventRecord[] events, IndexReadEventInfoResult result) {
		Assert.AreEqual(events.Length, result.EventInfos.Length);
		for (int i = 0; i < events.Length; i++) {
			Assert.AreEqual(events[i].EventNumber, result.EventInfos[i].EventNumber);
			Assert.AreEqual(events[i].LogPosition, result.EventInfos[i].LogPosition);
		}
	}

	protected override void WriteTestScenario() {
		var streamLast = 0L;
		var collidingStreamLast = 0L;

		for (int i = 0; i < _numEvents; i++) {
			if (_random.Next(2) == 0) {
				_events.Add(WriteSingleEvent(Stream, streamLast++, "test data"));
			} else {
				_events.Add(WriteSingleEvent(CollidingStream, collidingStreamLast++, "testing"));
			}
		}
	}

	[Test]
	public void returns_correct_events_before_position() {
		var curEvents = new List<EventRecord>();
		foreach (var @event in _events) {
			var result =
				ReadIndex.ReadEventInfoForward_KnownCollisions(Stream, 0, int.MaxValue, @event.LogPosition);
			CheckResult(curEvents.ToArray(), result);
			if (curEvents.Count == 0)
				Assert.True(result.IsEndOfStream);
			else
				Assert.AreEqual(int.MaxValue, result.NextEventNumber);

			if (@event.EventStreamId == Stream)
				curEvents.Add(@event);
		}
	}

	[Test]
	public void returns_correct_events_with_max_count() {
		var curEvents = new List<EventRecord>();

		foreach (var @event in _events) {
			if (@event.EventStreamId != Stream)
				continue;
			curEvents.Add(@event);

			int maxCount = Math.Min((int)@event.EventNumber + 1, _random.Next(10, 100));
			var fromEventNumber = @event.EventNumber - maxCount + 1;

			Assert.Greater(maxCount, 0);
			Assert.GreaterOrEqual(fromEventNumber, 0);

			var result = ReadIndex.ReadEventInfoForward_KnownCollisions(
				Stream, fromEventNumber, maxCount, long.MaxValue);
			CheckResult(curEvents.Skip(curEvents.Count - maxCount).ToArray(), result);
			Assert.AreEqual(@event.EventNumber + 1, result.NextEventNumber);
		}
	}
}
