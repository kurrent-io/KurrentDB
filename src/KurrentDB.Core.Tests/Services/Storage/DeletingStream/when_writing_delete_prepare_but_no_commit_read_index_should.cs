// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Core.TransactionLog.LogRecords;
using NUnit.Framework;
using ReadStreamResult = KurrentDB.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace KurrentDB.Core.Tests.Services.Storage.DeletingStream;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_writing_delete_prepare_but_no_commit_read_index_should<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord _event0;
	private EventRecord _event1;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		_event0 = await WriteSingleEvent("ES", 0, "bla1", token: token);
		Assert.True(_logFormat.StreamNameIndex.GetOrReserve("ES", out var esStreamId, out _, out _));
		var prepare = LogRecord.DeleteTombstone(_recordFactory, Writer.Position, Guid.NewGuid(), Guid.NewGuid(),
			esStreamId, eventTypeId, 1);

		Assert.IsTrue(await Writer.Write(prepare, token) is (true, _));

		_event1 = await WriteSingleEvent("ES", 1, "bla1", token: token);
	}

	[Test]
	public async Task indicate_that_stream_is_not_deleted() {
		Assert.That(await ReadIndex.IsStreamDeleted("ES", CancellationToken.None), Is.False);
	}

	[Test]
	public async Task indicate_that_nonexisting_stream_with_same_hash_is_not_deleted() {
		Assert.That(await ReadIndex.IsStreamDeleted("ZZ", CancellationToken.None), Is.False);
	}

	[Test]
	public async Task indicate_that_nonexisting_stream_with_different_hash_is_not_deleted() {
		Assert.That(await ReadIndex.IsStreamDeleted("XXX", CancellationToken.None), Is.False);
	}

	[Test]
	public async Task read_single_events_should_return_committed_records() {
		var result = await ReadIndex.ReadEvent("ES", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_event0, result.Record);

		result = await ReadIndex.ReadEvent("ES", 1, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_event1, result.Record);
	}

	[Test]
	public async Task read_stream_events_forward_should_return_committed_records() {
		var result = await ReadIndex.ReadStreamEventsForward("ES", 0, 100, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_event0, result.Records[0]);
		Assert.AreEqual(_event1, result.Records[1]);
	}

	[Test]
	public async Task read_stream_events_backward_should_return_committed_records() {
		var result = await ReadIndex.ReadStreamEventsBackward("ES", -1, 100, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_event0, result.Records[1]);
		Assert.AreEqual(_event1, result.Records[0]);
	}

	[Test]
	public async Task read_all_forward_should_return_all_stream_records_except_uncommitted() {
		var events = (await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 100, CancellationToken.None))
			.EventRecords()
			.Select(r => r.Event)
			.ToArray();
		Assert.AreEqual(2, events.Length);
		Assert.AreEqual(_event0, events[0]);
		Assert.AreEqual(_event1, events[1]);
	}

	[Test]
	public async Task read_all_backward_should_return_all_stream_records_except_uncommitted() {
		var events = (await ReadIndex.ReadAllEventsBackward(GetBackwardReadPos(), 100, CancellationToken.None))
			.EventRecords()
			.Select(r => r.Event)
			.ToArray();
		Assert.AreEqual(2, events.Length);
		Assert.AreEqual(_event1, events[0]);
		Assert.AreEqual(_event0, events[1]);
	}
}
