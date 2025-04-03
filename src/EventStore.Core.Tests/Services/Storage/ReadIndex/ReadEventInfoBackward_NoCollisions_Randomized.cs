// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Tests.Index.Hashers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.ReadIndex;

[TestFixture(3)]
[TestFixture(33)]
[TestFixture(123)]
[TestFixture(523)]
public class ReadEventInfoBackward_NoCollisions_Randomized : ReadIndexTestScenario<LogFormat.V2, string> {
	private const string Stream = "ab-1";
	private const ulong Hash = 98;
	private const string NonCollidingStream = "cd-1";

	private string GetStreamId(ulong hash) => hash == Hash ? Stream : throw new ArgumentException();

	private readonly Random _random = new Random();
	private readonly int _numEvents;
	private readonly List<EventRecord> _events;

	public ReadEventInfoBackward_NoCollisions_Randomized(int maxEntriesInMemTable) : base(
		chunkSize: 1_000_000,
		maxEntriesInMemTable: maxEntriesInMemTable,
		lowHasher: new ConstantHasher(0),
		highHasher: new HumanReadableHasher32()) {
		_numEvents = _random.Next(100, 400);
		_events = new List<EventRecord>(_numEvents);
	}

	private static void CheckResult(EventRecord[] events, IndexReadEventInfoResult result) {
		var eventInfos = result.EventInfos.Reverse().ToArray();
		Assert.AreEqual(events.Length, eventInfos.Length);
		for (int i = 0; i < events.Length; i++) {
			Assert.AreEqual(events[i].EventNumber, eventInfos[i].EventNumber);
			Assert.AreEqual(events[i].LogPosition, eventInfos[i].LogPosition);
		}
	}

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		var streamLast = 0L;
		var nonCollidingStreamLast = 0L;

		for (int i = 0; i < _numEvents; i++) {
			if (_random.Next(2) == 0) {
				_events.Add(await WriteSingleEvent(Stream, streamLast++, "test data", token: token));
			} else {
				_events.Add(await WriteSingleEvent(NonCollidingStream, nonCollidingStreamLast++, "testing", token: token));
			}
		}
	}

	[Test]
	public async Task returns_correct_events_before_position() {
		var curEvents = new List<EventRecord>();

		foreach (var @event in _events)
		{
			IndexReadEventInfoResult result;
			if (@event.EventStreamId == Stream) {
				result = await ReadIndex.ReadEventInfoBackward_NoCollisions(Hash, GetStreamId,
					@event.EventNumber - 1, int.MaxValue, @event.LogPosition, CancellationToken.None);
				CheckResult(curEvents.ToArray(), result);
				Assert.True(result.IsEndOfStream);

				// events >= @event.EventNumber should be filtered out
				result = await ReadIndex.ReadEventInfoBackward_NoCollisions(Hash, GetStreamId,
					@event.EventNumber, int.MaxValue, @event.LogPosition, CancellationToken.None);
				CheckResult(curEvents.ToArray(), result);
				Assert.True(result.IsEndOfStream);

				result = await ReadIndex.ReadEventInfoBackward_NoCollisions(Hash, GetStreamId,
					@event.EventNumber + 1, int.MaxValue, @event.LogPosition, CancellationToken.None);
				CheckResult(curEvents.ToArray(), result);
				Assert.True(result.IsEndOfStream);
			}

			result = await ReadIndex.ReadEventInfoBackward_NoCollisions(Hash, GetStreamId, -1, int.MaxValue,
				@event.LogPosition, CancellationToken.None);
			CheckResult(curEvents.ToArray(), result);
			Assert.True(result.IsEndOfStream);

			if (@event.EventStreamId == Stream)
				curEvents.Add(@event);
		}
	}

	[Test]
	public async Task returns_correct_events_with_max_count() {
		var curEvents = new List<EventRecord>();

		foreach (var @event in _events) {
			if (@event.EventStreamId != Stream) continue;
			curEvents.Add(@event);

			int maxCount = Math.Min((int)@event.EventNumber + 1, _random.Next(10, 100));
			var fromEventNumber = @event.EventNumber;

			Assert.Greater(maxCount, 0);
			Assert.GreaterOrEqual(fromEventNumber, 0);

			var result =
				await ReadIndex.ReadEventInfoBackward_NoCollisions(
					Hash, GetStreamId, fromEventNumber, maxCount, long.MaxValue, CancellationToken.None);
			CheckResult(curEvents.Skip(curEvents.Count - maxCount).ToArray(), result);

			if (fromEventNumber - maxCount < 0)
				Assert.True(result.IsEndOfStream);
			else
				Assert.AreEqual(fromEventNumber - maxCount, result.NextEventNumber);
		}
	}
}
