// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
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

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		var now = DateTime.UtcNow;

		var metadata1 = string.Format(@"{{""$maxAge"":{0}}}", (int)TimeSpan.FromMinutes(25).TotalSeconds);
		const string metadata2 = @"{""$maxCount"":2}";

		_r11 = await WriteStreamMetadata("ES1", 0, metadata1, token: token);
		_r21 = await WriteStreamMetadata("ES2", 0, metadata2, token: token);

		_r12 = await WriteSingleEvent("ES1", 0, "bla1", now.AddMinutes(-100), token: token);
		_r13 = await WriteSingleEvent("ES1", 1, "bla1", now.AddMinutes(-20), token: token);

		_r22 = await WriteSingleEvent("ES2", 0, "bla1", now.AddMinutes(-100), token: token);
		_r23 = await WriteSingleEvent("ES2", 1, "bla1", now.AddMinutes(-20), token: token);

		_r14 = await WriteSingleEvent("ES1", 2, "bla1", now.AddMinutes(-11), token: token);
		_r24 = await WriteSingleEvent("ES2", 2, "bla1", now.AddMinutes(-10), token: token);

		_r15 = await WriteSingleEvent("ES1", 3, "bla1", now.AddMinutes(-5), token: token);
		_r16 = await WriteSingleEvent("ES1", 4, "bla1", now.AddMinutes(-2), token: token);

		_r25 = await WriteSingleEvent("ES2", 3, "bla1", now.AddMinutes(-1), token: token);
		_r26 = await WriteSingleEvent("ES2", 4, "bla1", now.AddMinutes(-1), token: token);
	}

	[Test]
	public async Task single_event_read_doesnt_return_stream_created_event_for_both_streams() {
		var result = await ReadIndex.ReadEvent("ES1", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);

		result = await ReadIndex.ReadEvent("ES2", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task single_event_read_doesnt_return_expired_events_and_returns_all_actual_ones_for_stream_1() {
		var result = await ReadIndex.ReadEvent("ES1", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);

		result = await ReadIndex.ReadEvent("ES1", 1, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_r13, result.Record);

		result = await ReadIndex.ReadEvent("ES1", 2, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_r14, result.Record);

		result = await ReadIndex.ReadEvent("ES1", 3, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_r15, result.Record);

		result = await ReadIndex.ReadEvent("ES1", 4, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_r16, result.Record);
	}

	[Test]
	public async Task single_event_read_doesnt_return_expired_events_and_returns_all_actual_ones_for_stream_2() {
		var result = await ReadIndex.ReadEvent("ES2", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);

		result = await ReadIndex.ReadEvent("ES2", 1, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);

		result = await ReadIndex.ReadEvent("ES2", 2, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);

		result = await ReadIndex.ReadEvent("ES2", 3, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_r25, result.Record);

		result = await ReadIndex.ReadEvent("ES2", 4, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_r26, result.Record);
	}

	[Test]
	public async Task forward_range_read_doesnt_return_expired_records_for_stream_1() {
		var result = await ReadIndex.ReadStreamEventsForward("ES1", 0, 100, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(4, result.Records.Length);
		Assert.AreEqual(_r13, result.Records[0]);
		Assert.AreEqual(_r14, result.Records[1]);
		Assert.AreEqual(_r15, result.Records[2]);
		Assert.AreEqual(_r16, result.Records[3]);
	}

	[Test]
	public async Task forward_range_read_doesnt_return_expired_records_for_stream_2() {
		var result = await ReadIndex.ReadStreamEventsForward("ES2", 0, 100, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_r25, result.Records[0]);
		Assert.AreEqual(_r26, result.Records[1]);
	}

	[Test]
	public async Task backward_range_read_doesnt_return_expired_records_for_stream_1() {
		var result = await ReadIndex.ReadStreamEventsBackward("ES1", -1, 100, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(4, result.Records.Length);
		Assert.AreEqual(_r16, result.Records[0]);
		Assert.AreEqual(_r15, result.Records[1]);
		Assert.AreEqual(_r14, result.Records[2]);
		Assert.AreEqual(_r13, result.Records[3]);
	}

	[Test]
	public async Task backward_range_read_doesnt_return_expired_records_for_stream_2() {
		var result = await ReadIndex.ReadStreamEventsBackward("ES2", -1, 100, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_r26, result.Records[0]);
		Assert.AreEqual(_r25, result.Records[1]);
	}

	[Test]
	public async Task read_all_forward_returns_all_records_including_expired_ones() {
		var records = (await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 100, CancellationToken.None))
			.EventRecords();
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
	public async Task read_all_backward_returns_all_records_including_expired_ones() {
		var records = (await ReadIndex.ReadAllEventsBackward(GetBackwardReadPos(), 100, CancellationToken.None)).EventRecords();
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
