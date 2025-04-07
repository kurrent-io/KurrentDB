// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.Tests.Index.Hashers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.ReadIndex;

[TestFixture(3)]
[TestFixture(33)]
[TestFixture(123)]
[TestFixture(523)]
public class GetStreamLastEventNumber_KnownCollisions_Randomized : ReadIndexTestScenario<LogFormat.V2, string> {
	private const string Stream = "ab-1";
	private const string CollidingStream = "cb-1";

	private readonly Random _random = new Random();
	private readonly int _numEvents;
	private readonly List<EventRecord> _events;

	public GetStreamLastEventNumber_KnownCollisions_Randomized(int maxEntriesInMemTable) : base(
		chunkSize: 1_000_000,
		maxEntriesInMemTable: maxEntriesInMemTable,
		lowHasher: new ConstantHasher(0),
		highHasher: new HumanReadableHasher32()) {
		_numEvents = _random.Next(100, 400);
		_events = new List<EventRecord>(_numEvents);
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
	public void returns_correct_last_event_number_before_position() {
		var streamLast = ExpectedVersion.NoStream;
		var collidingStreamLast = ExpectedVersion.NoStream;

		foreach (var @event in _events) {
			Assert.AreEqual(streamLast,
				ReadIndex.GetStreamLastEventNumber_KnownCollisions(Stream, @event.LogPosition));

			Assert.AreEqual(collidingStreamLast,
				ReadIndex.GetStreamLastEventNumber_KnownCollisions(CollidingStream, @event.LogPosition));

			switch (@event.EventStreamId) {
				case Stream:
					streamLast = @event.EventNumber;
					break;
				case CollidingStream:
					collidingStreamLast = @event.EventNumber;
					break;
			}
		}
	}
}
