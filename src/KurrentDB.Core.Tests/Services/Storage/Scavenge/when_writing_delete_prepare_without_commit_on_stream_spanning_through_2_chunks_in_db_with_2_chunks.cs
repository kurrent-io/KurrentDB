// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services;
using KurrentDB.Core.TransactionLog.LogRecords;
using NUnit.Framework;
using ReadStreamResult = KurrentDB.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace KurrentDB.Core.Tests.Services.Storage.Scavenge;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_writing_delete_prepare_without_commit_and_scavenging<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord _event0;
	private EventRecord _event1;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		_event0 = await WriteSingleEvent("ES", 0, "bla1", token: token);
		var (esStreamId, _) = await GetOrReserve("ES", token);
		var (streamDeletedEventTypeId, pos) = await GetOrReserveEventType(SystemEventTypes.StreamDeleted, token);
		var prepare = LogRecord.DeleteTombstone(_recordFactory, pos, Guid.NewGuid(), Guid.NewGuid(),
			esStreamId, streamDeletedEventTypeId, 2);
		Assert.IsTrue(await Writer.Write(prepare, token) is (true, _));

		_event1 = await WriteSingleEvent("ES", 1, "bla1", token: token);
		Scavenge(completeLast: false, mergeChunks: false);
	}

	[Test]
	public async Task read_one_by_one_returns_all_committed_events() {
		var result = await ReadIndex.ReadEvent("ES", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_event0, result.Record);

		result = await ReadIndex.ReadEvent("ES", 1, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_event1, result.Record);
	}

	[Test]
	public async Task read_stream_events_forward_should_return_all_events() {
		var result = await ReadIndex.ReadStreamEventsForward("ES", 0, 100, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_event0, result.Records[0]);
		Assert.AreEqual(_event1, result.Records[1]);
	}

	[Test]
	public async Task read_stream_events_backward_should_return_stream_deleted() {
		var result = await ReadIndex.ReadStreamEventsBackward("ES", -1, 100, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_event1, result.Records[0]);
		Assert.AreEqual(_event0, result.Records[1]);
	}

	[Test]
	public async Task read_all_forward_returns_all_events() {
		var events = (await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 100, CancellationToken.None))
			.EventRecords()
			.Select(r => r.Event)
			.ToArray();
		Assert.AreEqual(2, events.Length);
		Assert.AreEqual(_event0, events[0]);
		Assert.AreEqual(_event1, events[1]);
	}

	[Test]
	public async Task read_all_backward_returns_all_events() {
		var events = (await ReadIndex.ReadAllEventsBackward(GetBackwardReadPos(), 100, CancellationToken.None))
			.EventRecords()
			.Select(r => r.Event)
			.ToArray();
		Assert.AreEqual(2, events.Length);
		Assert.AreEqual(_event1, events[0]);
		Assert.AreEqual(_event0, events[1]);
	}
}
