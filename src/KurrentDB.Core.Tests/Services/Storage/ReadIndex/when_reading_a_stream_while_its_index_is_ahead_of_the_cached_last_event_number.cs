// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.Storage.ReadIndex;

// Reproduces the race in https://github.com/kurrent-io/KurrentDB/issues/5816.
// A read takes the last event number from the cache, then reads the requested range from the index,
// so an append that completes in between leaves the index ahead of the last event number the read holds.
// The scenario sets up that state directly: events 0..8 are committed and cached as the last event number,
// and events 9..10 are in the index but not in the cache.
[TestFixture(typeof(LogFormat.V2), typeof(string))]
public class when_reading_a_stream_while_its_index_is_ahead_of_the_cached_last_event_number<TLogFormat, TStreamId>
	: ReadIndexTestScenario<TLogFormat, TStreamId> {
	private const string PlainStream = "plain-stream";
	private const string MaxAgeStream = "max-age-stream";
	private const long CachedLastEventNumber = 8;

	private IPrepareLogRecord<TStreamId>[] _plainStreamAppended;
	private IPrepareLogRecord<TStreamId>[] _maxAgeStreamAppended;

	public when_reading_a_stream_while_its_index_is_ahead_of_the_cached_last_event_number()
		: base(maxEntriesInMemTable: 100, streamInfoCacheCapacity: 100) { }

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		await WriteStreamMetadata(MaxAgeStream, 0, @"{""$maxAge"":86400}", token: token);

		_plainStreamAppended = await WriteStream(PlainStream, token);
		_maxAgeStreamAppended = await WriteStream(MaxAgeStream, token);
	}

	// writes committed events 0..8, then the prepares of events 9..10 without a commit,
	// so that building the index leaves them out.
	private async ValueTask<IPrepareLogRecord<TStreamId>[]> WriteStream(string stream, CancellationToken token) {
		for (var eventNumber = 0; eventNumber <= CachedLastEventNumber; eventNumber++)
			await WriteSingleEvent(stream, eventNumber, $"data-{eventNumber}", token: token);

		return [
			await WritePrepare(stream, expectedVersion: CachedLastEventNumber, token: token),
			await WritePrepare(stream, expectedVersion: CachedLastEventNumber + 1, token: token),
		];
	}

	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		await AppendToIndexOnly(PlainStream, _plainStreamAppended);
		await AppendToIndexOnly(MaxAgeStream, _maxAgeStreamAppended);
	}

	// caches the last event number, then adds the index entries of an append without updating the cache.
	private async Task AppendToIndexOnly(string stream, IPrepareLogRecord<TStreamId>[] prepares) {
		Assert.AreEqual(CachedLastEventNumber, await ReadIndex.GetStreamLastEventNumber(stream, CancellationToken.None));

		for (var i = 0; i < prepares.Length; i++) {
			var prepare = prepares[i];
			TableIndex.Add(prepare.LogPosition, prepare.EventStreamId, CachedLastEventNumber + 1 + i, prepare.LogPosition);
		}

		Assert.AreEqual(CachedLastEventNumber, await ReadIndex.GetStreamLastEventNumber(stream, CancellationToken.None));
	}

	private static void AssertConsistent(IndexReadStreamResult result, long[] expectedEventNumbers) {
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(CachedLastEventNumber, result.LastEventNumber);
		CollectionAssert.AreEqual(expectedEventNumbers, result.Records.Select(x => x.EventNumber).ToArray());
	}

	private static long[] Range(long from, long to) =>
		from <= to
			? Enumerable.Range((int)from, (int)(to - from + 1)).Select(x => (long)x).ToArray()
			: Enumerable.Range((int)to, (int)(from - to + 1)).Select(x => (long)x).Reverse().ToArray();

	[TestCase(PlainStream)]
	[TestCase(MaxAgeStream)]
	public async Task forward_read_past_the_end_returns_only_events_up_to_the_last_event_number(string stream) {
		var result = await ReadIndex.ReadStreamEventsForward(stream, 0, 200, CancellationToken.None);

		AssertConsistent(result, Range(0, CachedLastEventNumber));
		Assert.AreEqual(CachedLastEventNumber + 1, result.NextEventNumber);
		Assert.IsTrue(result.IsEndOfStream);
	}

	[TestCase(PlainStream)]
	[TestCase(MaxAgeStream)]
	public async Task forward_read_of_a_page_ending_past_the_last_event_number_is_capped(string stream) {
		var result = await ReadIndex.ReadStreamEventsForward(stream, 5, 5, CancellationToken.None);

		AssertConsistent(result, Range(5, CachedLastEventNumber));
		Assert.AreEqual(CachedLastEventNumber + 1, result.NextEventNumber);
		Assert.IsTrue(result.IsEndOfStream);
	}

	[TestCase(PlainStream)]
	[TestCase(MaxAgeStream)]
	public async Task forward_read_within_the_last_event_number_is_unchanged(string stream) {
		var result = await ReadIndex.ReadStreamEventsForward(stream, 0, 5, CancellationToken.None);

		AssertConsistent(result, Range(0, 4));
		Assert.AreEqual(5, result.NextEventNumber);
		Assert.IsFalse(result.IsEndOfStream);
	}

	[TestCase(PlainStream)]
	[TestCase(MaxAgeStream)]
	public async Task backward_read_from_past_the_last_event_number_starts_at_the_last_event_number(string stream) {
		var result = await ReadIndex.ReadStreamEventsBackward(stream, CachedLastEventNumber + 2, 200, CancellationToken.None);

		AssertConsistent(result, Range(CachedLastEventNumber, 0));
		Assert.IsTrue(result.IsEndOfStream);
	}

	[TestCase(PlainStream)]
	[TestCase(MaxAgeStream)]
	public async Task backward_read_from_the_end_starts_at_the_last_event_number(string stream) {
		var result = await ReadIndex.ReadStreamEventsBackward(stream, -1, 200, CancellationToken.None);

		AssertConsistent(result, Range(CachedLastEventNumber, 0));
		Assert.IsTrue(result.IsEndOfStream);
	}
}
