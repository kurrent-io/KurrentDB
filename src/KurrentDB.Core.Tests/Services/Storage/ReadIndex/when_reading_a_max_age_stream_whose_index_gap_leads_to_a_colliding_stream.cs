// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Index;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.Tests.Index.Hashers;
using KurrentDB.Core.Tests.Services.Storage.MaxAgeMaxCount.AfterScavenge;
using KurrentDB.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.Storage.ReadIndex;

// A $maxAge read that starts in a gap in the index looks for the next index entry with TryGetNextEntry.
// That lookup matches on the stream hash and checks the oldest index table first, so it can return an entry
// of a colliding stream with a version past the stream's last event number.
// The read must still return the stream's last event, and never an event past its last event number.
//
// "ab-1" and "cb-1" collide. "cb-1" has events 9..28 (its earlier events are gone, as after a scavenge),
// filling the first memtable so they end up in the oldest index table.
// "ab-1" has $maxAge and events 0..8, of which 0..7 are expired and removed from the index, as after a scavenge.
public abstract class when_reading_a_max_age_stream_whose_index_gap_leads_to_a_colliding_stream
	: ReadIndexTestScenario<LogFormat.V2, string> {
	private const string Stream = "ab-1";
	private const string CollidingStream = "cb-1";
	private const long LastEventNumber = 8;

	private readonly HashSet<long> _scavengedPositions = [];
	private IPrepareLogRecord<string>[] _appended;

	protected when_reading_a_max_age_stream_whose_index_gap_leads_to_a_colliding_stream() : base(
		maxEntriesInMemTable: 20,
		performAdditionalChecks: false,
		lowHasher: new ConstantHasher(0),
		// keeps the stream's cached last event number from being evicted, so the append-during-read fixture can hold it at 8.
		streamInfoCacheCapacity: 100,
		highHasher: new HumanReadableHasher32()) { }

	// whether events 9..10 of the stream are in the index but not yet in the cached last event number,
	// as when an append completes while the read runs.
	protected abstract bool AppendDuringRead { get; }

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		var now = DateTime.UtcNow;
		var expired = now.AddMinutes(-50);

		for (var eventNumber = 9; eventNumber <= 28; eventNumber++)
			await WriteSingleEvent(CollidingStream, eventNumber, "data", now, token: token);

		await WriteStreamMetadata(Stream, 0, @"{""$maxAge"":600}", token: token);

		for (var eventNumber = 0; eventNumber < LastEventNumber; eventNumber++)
			_scavengedPositions.Add((await WriteSingleEvent(Stream, eventNumber, "data", expired, token: token)).LogPosition);

		await WriteSingleEvent(Stream, LastEventNumber, "data", now, token: token);

		_appended = [
			await WritePrepare(Stream, expectedVersion: LastEventNumber, token: token),
			await WritePrepare(Stream, expectedVersion: LastEventNumber + 1, token: token),
		];
	}

	protected override ITableIndex<string> TransformTableIndex(ITableIndex<string> tableIndex) =>
		new FilteredTableIndex<string>(tableIndex, entry => !_scavengedPositions.Contains(entry.Position));

	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		Assert.AreEqual(LastEventNumber, await ReadIndex.GetStreamLastEventNumber(Stream, CancellationToken.None));

		if (AppendDuringRead) {
			for (var i = 0; i < _appended.Length; i++) {
				var prepare = _appended[i];
				TableIndex.Add(prepare.LogPosition, prepare.EventStreamId, LastEventNumber + 1 + i, prepare.LogPosition);
			}

			Assert.AreEqual(LastEventNumber, await ReadIndex.GetStreamLastEventNumber(Stream, CancellationToken.None));
		}
	}

	[Test]
	public void the_index_gap_leads_to_the_colliding_stream() {
		Assert.AreEqual(Hasher.Hash(Stream), Hasher.Hash(CollidingStream));
		Assert.IsTrue(TableIndex.TryGetNextEntry(Stream, 0, out var next));
		Assert.AreEqual(9, next.Version);
	}

	[Test]
	public async Task reading_forwards_from_the_gap_returns_the_last_event_and_nothing_past_it() {
		var fromEventNumber = 0L;
		var received = new List<long>();

		for (var i = 0; i < 20; i++) {
			var result = await ReadIndex.ReadStreamEventsForward(Stream, fromEventNumber, 5, CancellationToken.None);

			Assert.AreEqual(ReadStreamResult.Success, result.Result);
			Assert.AreEqual(LastEventNumber, result.LastEventNumber);
			received.AddRange(result.Records.Select(x => x.EventNumber));

			if (result.IsEndOfStream)
				break;

			fromEventNumber = result.NextEventNumber;
		}

		CollectionAssert.AreEqual(new[] { LastEventNumber }, received);
	}

	public class without_an_append_during_the_read : when_reading_a_max_age_stream_whose_index_gap_leads_to_a_colliding_stream {
		protected override bool AppendDuringRead => false;
	}

	public class with_an_append_during_the_read : when_reading_a_max_age_stream_whose_index_gap_leads_to_a_colliding_stream {
		protected override bool AppendDuringRead => true;
	}
}
