// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.MaxAgeMaxCount;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_having_one_stream_with_maxage_and_other_stream_with_maxcount_and_streams_have_same_hash<TLogFormat, TStreamId> :
		ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord _r11;
	private EventRecord _r12;
	private EventRecord _r13;
	private EventRecord _r14;
	private EventRecord _r15;
	private EventRecord _r16;

	private EventRecord _r21;
	private EventRecord _r22;
	private EventRecord _r23;
	private EventRecord _r24;
	private EventRecord _r25;
	private EventRecord _r26;

	protected override void WriteTestScenario() {
		var now = DateTime.UtcNow;

		var metadata1 = string.Format(@"{{""$maxAge"":{0}}}", (int)TimeSpan.FromMinutes(25).TotalSeconds);
		const string metadata2 = @"{""$maxCount"":2}";

		_r11 = WriteStreamMetadata("ES1", 0, metadata1);
		_r21 = WriteStreamMetadata("ES2", 0, metadata2);

		_r12 = WriteSingleEvent("ES1", 0, "bla1", now.AddMinutes(-100));
		_r13 = WriteSingleEvent("ES1", 1, "bla1", now.AddMinutes(-20));

		_r22 = WriteSingleEvent("ES2", 0, "bla1", now.AddMinutes(-100));
		_r23 = WriteSingleEvent("ES2", 1, "bla1", now.AddMinutes(-20));

		_r14 = WriteSingleEvent("ES1", 2, "bla1", now.AddMinutes(-11));
		_r24 = WriteSingleEvent("ES2", 2, "bla1", now.AddMinutes(-10));

		_r15 = WriteSingleEvent("ES1", 3, "bla1", now.AddMinutes(-5));
		_r16 = WriteSingleEvent("ES1", 4, "bla1", now.AddMinutes(-2));

		_r25 = WriteSingleEvent("ES2", 3, "bla1", now.AddMinutes(-1));
		_r26 = WriteSingleEvent("ES2", 4, "bla1", now.AddMinutes(-1));
	}

	[Test]
	public void single_event_read_doesnt_return_stream_created_event_for_both_streams() {
		var result = ReadIndex.ReadEvent("ES1", 0);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);

		result = ReadIndex.ReadEvent("ES2", 0);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public void single_event_read_doesnt_return_expired_events_and_returns_all_actual_ones_for_stream_1() {
		var result = ReadIndex.ReadEvent("ES1", 0);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);

		result = ReadIndex.ReadEvent("ES1", 1);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_r13, result.Record);

		result = ReadIndex.ReadEvent("ES1", 2);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_r14, result.Record);

		result = ReadIndex.ReadEvent("ES1", 3);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_r15, result.Record);

		result = ReadIndex.ReadEvent("ES1", 4);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_r16, result.Record);
	}

	[Test]
	public void single_event_read_doesnt_return_expired_events_and_returns_all_actual_ones_for_stream_2() {
		var result = ReadIndex.ReadEvent("ES2", 0);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);

		result = ReadIndex.ReadEvent("ES2", 1);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);

		result = ReadIndex.ReadEvent("ES2", 2);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);

		result = ReadIndex.ReadEvent("ES2", 3);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_r25, result.Record);

		result = ReadIndex.ReadEvent("ES2", 4);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_r26, result.Record);
	}

	[Test]
	public void forward_range_read_doesnt_return_expired_records_for_stream_1() {
		var result = ReadIndex.ReadStreamEventsForward("ES1", 0, 100);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(4, result.Records.Length);
		Assert.AreEqual(_r13, result.Records[0]);
		Assert.AreEqual(_r14, result.Records[1]);
		Assert.AreEqual(_r15, result.Records[2]);
		Assert.AreEqual(_r16, result.Records[3]);
	}

	[Test]
	public void forward_range_read_doesnt_return_expired_records_for_stream_2() {
		var result = ReadIndex.ReadStreamEventsForward("ES2", 0, 100);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_r25, result.Records[0]);
		Assert.AreEqual(_r26, result.Records[1]);
	}

	[Test]
	public void backward_range_read_doesnt_return_expired_records_for_stream_1() {
		var result = ReadIndex.ReadStreamEventsBackward("ES1", -1, 100);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(4, result.Records.Length);
		Assert.AreEqual(_r16, result.Records[0]);
		Assert.AreEqual(_r15, result.Records[1]);
		Assert.AreEqual(_r14, result.Records[2]);
		Assert.AreEqual(_r13, result.Records[3]);
	}

	[Test]
	public void backward_range_read_doesnt_return_expired_records_for_stream_2() {
		var result = ReadIndex.ReadStreamEventsBackward("ES2", -1, 100);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_r26, result.Records[0]);
		Assert.AreEqual(_r25, result.Records[1]);
	}

	[Test]
	public void read_all_forward_returns_all_records_including_expired_ones() {
		var records = ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 100).EventRecords();
		Assert.AreEqual(12, records.Count);
		Assert.AreEqual(_r11, records[0].Event);
		Assert.AreEqual(_r21, records[1].Event);

		Assert.AreEqual(_r12, records[2].Event);
		Assert.AreEqual(_r13, records[3].Event);

		Assert.AreEqual(_r22, records[4].Event);
		Assert.AreEqual(_r23, records[5].Event);

		Assert.AreEqual(_r14, records[6].Event);
		Assert.AreEqual(_r24, records[7].Event);

		Assert.AreEqual(_r15, records[8].Event);
		Assert.AreEqual(_r16, records[9].Event);

		Assert.AreEqual(_r25, records[10].Event);
		Assert.AreEqual(_r26, records[11].Event);
	}

	[Test]
	public void read_all_backward_returns_all_records_including_expired_ones() {
		var records = ReadIndex.ReadAllEventsBackward(GetBackwardReadPos(), 100).EventRecords();
		Assert.AreEqual(12, records.Count);
		Assert.AreEqual(_r11, records[11].Event);
		Assert.AreEqual(_r21, records[10].Event);

		Assert.AreEqual(_r12, records[9].Event);
		Assert.AreEqual(_r13, records[8].Event);

		Assert.AreEqual(_r22, records[7].Event);
		Assert.AreEqual(_r23, records[6].Event);

		Assert.AreEqual(_r14, records[5].Event);
		Assert.AreEqual(_r24, records[4].Event);

		Assert.AreEqual(_r15, records[3].Event);
		Assert.AreEqual(_r16, records[2].Event);

		Assert.AreEqual(_r25, records[1].Event);
		Assert.AreEqual(_r26, records[0].Event);
	}
}
