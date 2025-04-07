// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.DeletingStream;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_writing_delete_prepare_but_no_commit_read_index_should<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord _event0;
	private EventRecord _event1;

	protected override void WriteTestScenario() {
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		_event0 = WriteSingleEvent("ES", 0, "bla1");
		Assert.True(_logFormat.StreamNameIndex.GetOrReserve("ES", out var esStreamId, out _, out _));
		var prepare = LogRecord.DeleteTombstone(_recordFactory, Writer.Position, Guid.NewGuid(), Guid.NewGuid(),
			esStreamId, eventTypeId, 1);
		long pos;
		Assert.IsTrue(Writer.Write(prepare, out pos));

		_event1 = WriteSingleEvent("ES", 1, "bla1");
	}

	[Test]
	public void indicate_that_stream_is_not_deleted() {
		Assert.That(ReadIndex.IsStreamDeleted("ES"), Is.False);
	}

	[Test]
	public void indicate_that_nonexisting_stream_with_same_hash_is_not_deleted() {
		Assert.That(ReadIndex.IsStreamDeleted("ZZ"), Is.False);
	}

	[Test]
	public void indicate_that_nonexisting_stream_with_different_hash_is_not_deleted() {
		Assert.That(ReadIndex.IsStreamDeleted("XXX"), Is.False);
	}

	[Test]
	public void read_single_events_should_return_commited_records() {
		var result = ReadIndex.ReadEvent("ES", 0);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_event0, result.Record);

		result = ReadIndex.ReadEvent("ES", 1);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_event1, result.Record);
	}

	[Test]
	public void read_stream_events_forward_should_return_commited_records() {
		var result = ReadIndex.ReadStreamEventsForward("ES", 0, 100);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_event0, result.Records[0]);
		Assert.AreEqual(_event1, result.Records[1]);
	}

	[Test]
	public void read_stream_events_backward_should_return_commited_records() {
		var result = ReadIndex.ReadStreamEventsBackward("ES", -1, 100);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_event0, result.Records[1]);
		Assert.AreEqual(_event1, result.Records[0]);
	}

	[Test]
	public void read_all_forward_should_return_all_stream_records_except_uncommited() {
		var events = ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 100).EventRecords()
			.Select(r => r.Event)
			.ToArray();
		Assert.AreEqual(2, events.Length);
		Assert.AreEqual(_event0, events[0]);
		Assert.AreEqual(_event1, events[1]);
	}

	[Test]
	public void read_all_backward_should_return_all_stream_records_except_uncommited() {
		var events = ReadIndex.ReadAllEventsBackward(GetBackwardReadPos(), 100).EventRecords()
			.Select(r => r.Event)
			.ToArray();
		Assert.AreEqual(2, events.Length);
		Assert.AreEqual(_event1, events[0]);
		Assert.AreEqual(_event0, events[1]);
	}
}
