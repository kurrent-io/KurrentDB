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
public class with_invalid_metadata<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord _r1;
	private EventRecord _r2;
	private EventRecord _r3;
	private EventRecord _r4;
	private EventRecord _r5;
	private EventRecord _r6;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		var now = DateTime.UtcNow;

		const string metadata = @"{""$maxCount"":4,,""$maxAge"":}";

		_r1 = await WriteStreamMetadata("ES", 0, metadata, now.AddSeconds(-100), token: token);
		_r2 = await WriteSingleEvent("ES", 0, "bla1", now.AddSeconds(-50), token: token);
		_r3 = await WriteSingleEvent("ES", 1, "bla1", now.AddSeconds(-20), token: token);
		_r4 = await WriteSingleEvent("ES", 2, "bla1", now.AddSeconds(-11), token: token);
		_r5 = await WriteSingleEvent("ES", 3, "bla1", now.AddSeconds(-5), token: token);
		_r6 = await WriteSingleEvent("ES", 4, "bla1", now.AddSeconds(-1), token: token);
	}

	[Test]
	public async Task on_single_event_read_all_metadata_is_ignored() {
		var result = await ReadIndex.ReadEvent("ES", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_r2, result.Record);

		result = await ReadIndex.ReadEvent("ES", 1, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_r3, result.Record);

		result = await ReadIndex.ReadEvent("ES", 2, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_r4, result.Record);

		result = await ReadIndex.ReadEvent("ES", 3, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_r5, result.Record);

		result = await ReadIndex.ReadEvent("ES", 4, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_r6, result.Record);
	}

	[Test]
	public async Task on_forward_range_read_all_metadata_is_ignored() {
		var result = await ReadIndex.ReadStreamEventsForward("ES", 0, 100, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(5, result.Records.Length);
		Assert.AreEqual(_r2, result.Records[0]);
		Assert.AreEqual(_r3, result.Records[1]);
		Assert.AreEqual(_r4, result.Records[2]);
		Assert.AreEqual(_r5, result.Records[3]);
		Assert.AreEqual(_r6, result.Records[4]);
	}

	[Test]
	public async Task on_backward_range_read_all_metadata_is_ignored() {
		var result = await ReadIndex.ReadStreamEventsBackward("ES", -1, 100, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(5, result.Records.Length);
		Assert.AreEqual(_r2, result.Records[4]);
		Assert.AreEqual(_r3, result.Records[3]);
		Assert.AreEqual(_r4, result.Records[2]);
		Assert.AreEqual(_r5, result.Records[1]);
		Assert.AreEqual(_r6, result.Records[0]);
	}

	[Test]
	public async Task on_read_all_forward_all_metadata_is_ignored() {
		var records = (await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 100, CancellationToken.None))
			.EventRecords();
		Assert.AreEqual(6, records.Count);
		Assert.AreEqual(_r1, records[0].Event);
		Assert.AreEqual(_r2, records[1].Event);
		Assert.AreEqual(_r3, records[2].Event);
		Assert.AreEqual(_r4, records[3].Event);
		Assert.AreEqual(_r5, records[4].Event);
		Assert.AreEqual(_r6, records[5].Event);
	}

	[Test]
	public async Task on_read_all_backward_all_metadata_is_ignored() {
		var records = (await ReadIndex.ReadAllEventsBackward(GetBackwardReadPos(), 100, CancellationToken.None))
			.EventRecords();
		Assert.AreEqual(6, records.Count);
		Assert.AreEqual(_r6, records[0].Event);
		Assert.AreEqual(_r5, records[1].Event);
		Assert.AreEqual(_r4, records[2].Event);
		Assert.AreEqual(_r3, records[3].Event);
		Assert.AreEqual(_r2, records[4].Event);
		Assert.AreEqual(_r1, records[5].Event);
	}
}
