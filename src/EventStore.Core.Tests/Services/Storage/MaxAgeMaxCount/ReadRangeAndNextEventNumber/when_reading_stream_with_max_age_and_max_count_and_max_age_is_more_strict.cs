// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.MaxAgeMaxCount.ReadRangeAndNextEventNumber;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_reading_stream_with_max_age_and_max_count_and_max_age_is_more_strict<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord _event3;
	private EventRecord _event4;
	private EventRecord _event5;


	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		var now = DateTime.UtcNow;

		var metadata = string.Format(@"{{""$maxAge"":{0},""$maxCount"":5}}",
			(int)TimeSpan.FromMinutes(20).TotalSeconds);

		await WriteStreamMetadata("ES", 0, metadata, token: token);
		await WriteSingleEvent("ES", 0, "bla", now.AddMinutes(-100), token: token); // expired: maxcount & maxage
		await WriteSingleEvent("ES", 1, "bla", now.AddMinutes(-50), token: token); // expired: maxage
		await WriteSingleEvent("ES", 2, "bla", now.AddMinutes(-25), token: token); // expired: maxage
		_event3 = await WriteSingleEvent("ES", 3, "bla", now.AddMinutes(-15), token: token); // active
		_event4 = await WriteSingleEvent("ES", 4, "bla", now.AddMinutes(-11), token: token); // active
		_event5 = await WriteSingleEvent("ES", 5, "bla", now.AddMinutes(-3), token: token); // active
	}

	[Test]
	public async Task
		on_read_forward_from_start_to_expired_next_event_number_is_expired_by_age_plus_1_and_its_not_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsForward("ES", 0, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(3, res.NextEventNumber); // new path fast forwards to here
		Assert.AreEqual(5, res.LastEventNumber);
		Assert.IsFalse(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(0, records.Length);
	}

	[Test]
	public async Task
		on_read_forward_from_start_to_active_next_event_number_is_last_read_event_plus_1_and_its_not_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsForward("ES", 0, 5, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(5, res.NextEventNumber);
		Assert.AreEqual(5, res.LastEventNumber);
		Assert.IsFalse(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(2, records.Length);
		Assert.AreEqual(_event3, records[0]);
		Assert.AreEqual(_event4, records[1]);
	}

	[Test]
	public async Task
		on_read_forward_from_expired_to_active_next_event_number_is_last_read_event_plus_1_and_its_not_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsForward("ES", 2, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(4, res.NextEventNumber);
		Assert.AreEqual(5, res.LastEventNumber);
		Assert.IsFalse(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(1, records.Length);
		Assert.AreEqual(_event3, records[0]);
	}

	[Test]
	public async Task on_read_forward_from_expired_to_end_next_event_number_is_end_plus_1_and_its_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsForward("ES", 2, 4, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(6, res.NextEventNumber);
		Assert.AreEqual(5, res.LastEventNumber);
		Assert.IsTrue(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(3, records.Length);
		Assert.AreEqual(_event3, records[0]);
		Assert.AreEqual(_event4, records[1]);
		Assert.AreEqual(_event5, records[2]);
	}

	[Test]
	public async Task
		on_read_forward_from_expired_to_out_of_bounds_next_event_number_is_end_plus_1_and_its_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsForward("ES", 2, 6, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(6, res.NextEventNumber);
		Assert.AreEqual(5, res.LastEventNumber);
		Assert.IsTrue(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(3, records.Length);
		Assert.AreEqual(_event3, records[0]);
		Assert.AreEqual(_event4, records[1]);
		Assert.AreEqual(_event5, records[2]);
	}

	[Test]
	public async Task
		on_read_forward_from_out_of_bounds_to_out_of_bounds_next_event_number_is_end_plus_1_and_its_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsForward("ES", 7, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(6, res.NextEventNumber);
		Assert.AreEqual(5, res.LastEventNumber);
		Assert.IsTrue(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(0, records.Length);
	}


	[Test]
	public async Task
		on_read_backward_from_end_to_active_next_event_number_is_last_read_event_minus_1_and_its_not_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsBackward("ES", 5, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(3, res.NextEventNumber);
		Assert.AreEqual(5, res.LastEventNumber);
		Assert.IsFalse(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(2, records.Length);
		Assert.AreEqual(_event5, records[0]);
		Assert.AreEqual(_event4, records[1]);
	}

	[Test]
	public async Task
		on_read_backward_from_end_to_maxage_bound_next_event_number_is_maxage_bound_minus_1_and_its_not_end_of_stream() // just no simple way to tell this
	{
		var res = await ReadIndex.ReadStreamEventsBackward("ES", 5, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(2, res.NextEventNumber);
		Assert.AreEqual(5, res.LastEventNumber);
		Assert.IsFalse(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(3, records.Length);
		Assert.AreEqual(_event5, records[0]);
		Assert.AreEqual(_event4, records[1]);
		Assert.AreEqual(_event3, records[2]);
	}

	[Test]
	public async Task on_read_backward_from_active_to_expired_its_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsBackward("ES", 4, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(-1, res.NextEventNumber);
		Assert.AreEqual(5, res.LastEventNumber);
		Assert.IsTrue(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(2, records.Length);
		Assert.AreEqual(_event4, records[0]);
		Assert.AreEqual(_event3, records[1]);
	}

	[Test]
	public async Task on_read_backward_from_expired_to_expired_its_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsBackward("ES", 2, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(-1, res.NextEventNumber);
		Assert.AreEqual(5, res.LastEventNumber);
		Assert.IsTrue(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(0, records.Length);
	}

	[Test]
	public async Task on_read_backward_from_expired_to_before_start_its_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsBackward("ES", 2, 5, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(-1, res.NextEventNumber);
		Assert.AreEqual(5, res.LastEventNumber);
		Assert.IsTrue(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(0, records.Length);
	}

	[Test]
	public async Task
		on_read_backward_from_out_of_bounds_to_out_of_bounds_next_event_number_is_end_and_its_not_end_of_stream() {
		var res = await ReadIndex.ReadStreamEventsBackward("ES", 10, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, res.Result);
		Assert.AreEqual(5, res.NextEventNumber);
		Assert.AreEqual(5, res.LastEventNumber);
		Assert.IsFalse(res.IsEndOfStream);

		var records = res.Records;
		Assert.AreEqual(0, records.Length);
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_reading_stream_with_max_age_and_a_mostly_expired<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat,
		TStreamId> {

	public when_reading_stream_with_max_age_and_a_mostly_expired() : base(maxEntriesInMemTable: 50,
		chunkSize: 100000) {

	}

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		var now = DateTime.UtcNow;

		var metadata = string.Format(@"{{""$maxAge"":{0}}}",
			(int)TimeSpan.FromMinutes(20).TotalSeconds);

		await WriteStreamMetadata("ES", 0, metadata, token: token);
		var start = 0;
		var end = 1008;
		for (int i = start; i < end; i++) {
			await WriteSingleEvent("ES", i, "bla", now.AddMinutes(-100), retryOnFail: true, token: token);
		}

		start = end;
		end += 5;
		for (int i = start; i < end; i++) {
			await WriteSingleEvent("ES", i, "bla", now, retryOnFail: true, token: token);
		}

	}

	[Test]
	public async Task reading_forward_should_match_reading_backwards_in_reverse() {
		var backwardsCollected = new List<EventRecord>();
		var forwardsCollected = new List<EventRecord>();
		var from = long.MaxValue;
		while (true) {
			var backwards = await ReadIndex.ReadStreamEventsBackward("ES", from, 7, CancellationToken.None);
			for (int i = 0; i < backwards.Records.Length; i++) {
				backwardsCollected.Add(backwards.Records[i]);
			}

			from = backwards.NextEventNumber;
			if (backwards.IsEndOfStream) break;
		}

		from = 0;
		while (true) {
			var forwards = await ReadIndex.ReadStreamEventsForward("ES", from, 7, CancellationToken.None);
			for (int i = 0; i < forwards.Records.Length; i++) {
				forwardsCollected.Add(forwards.Records[i]);
			}

			from = forwards.NextEventNumber;
			if (forwards.IsEndOfStream) break;
		}

		Assert.AreEqual(forwardsCollected.Count, backwardsCollected.Count);
		backwardsCollected.Reverse();
		for (int i = 0; i < backwardsCollected.Count; i++) {
			Assert.AreEqual(backwardsCollected[i].EventId, forwardsCollected[i].EventId);
			Assert.AreEqual(backwardsCollected[i].EventType, forwardsCollected[i].EventType);
			Assert.AreEqual(backwardsCollected[i].ExpectedVersion, forwardsCollected[i].ExpectedVersion);
			Assert.AreEqual(backwardsCollected[i].EventNumber, forwardsCollected[i].EventNumber);
		}
	}
}
