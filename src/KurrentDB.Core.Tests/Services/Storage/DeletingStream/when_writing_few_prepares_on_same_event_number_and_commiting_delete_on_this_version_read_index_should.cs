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

namespace KurrentDB.Core.Tests.Services.Storage.DeletingStream;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_writing_few_prepares_with_same_event_number_and_committing_delete_on_this_version_read_index_should<TLogFormat, TStreamId> :
		ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord _deleteTombstone;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		string stream = "ES";
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		var (streamId, _) = await GetOrReserve(stream, token);
		var (streamDeletedEventTypeId, pos) = await GetOrReserveEventType(SystemEventTypes.StreamDeleted, token);

		var prepare1 = LogRecord.SingleWrite(_recordFactory, pos, // prepare1
			Guid.NewGuid(),
			Guid.NewGuid(),
			streamId,
			-1,
			eventTypeId,
			LogRecord.NoData,
			null,
			DateTime.UtcNow);

		(var written, pos) = await Writer.Write(prepare1, token);
		Assert.IsTrue(written);

		var prepare2 = LogRecord.SingleWrite(_recordFactory, pos, // prepare2
			Guid.NewGuid(),
			Guid.NewGuid(),
			streamId,
			-1,
			eventTypeId,
			LogRecord.NoData,
			null,
			DateTime.UtcNow);

		(written, pos) = await Writer.Write(prepare2, token);
		Assert.IsTrue(written);


		var deletePrepare = LogRecord.DeleteTombstone(_recordFactory, pos, // delete prepare
			Guid.NewGuid(), Guid.NewGuid(), streamId, streamDeletedEventTypeId, -1);
		_deleteTombstone = new EventRecord(EventNumber.DeletedStream, deletePrepare, stream, SystemEventTypes.StreamDeleted);

		(written, pos) = await Writer.Write(deletePrepare, token);
		Assert.IsTrue(written);

		var prepare3 = LogRecord.SingleWrite(_recordFactory, pos, // prepare3
			Guid.NewGuid(),
			Guid.NewGuid(),
			streamId,
			-1,
			eventTypeId,
			LogRecord.NoData,
			null,
			DateTime.UtcNow);

		(written, pos) = await Writer.Write(prepare3, token);
		Assert.IsTrue(written);

		var commit = LogRecord.Commit(pos, // committing delete
			deletePrepare.CorrelationId,
			deletePrepare.LogPosition,
			EventNumber.DeletedStream);
		Assert.IsTrue(await Writer.Write(commit, token) is (true, _));
	}

	[Test]
	public async Task indicate_that_stream_is_deleted() {
		Assert.That(await ReadIndex.IsStreamDeleted("ES", CancellationToken.None));
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
	public async Task read_single_events_with_number_0_should_return_stream_deleted() {
		var result = await ReadIndex.ReadEvent("ES", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.StreamDeleted, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task read_single_events_with_number_1_should_return_stream_deleted() {
		var result = await ReadIndex.ReadEvent("ES", 1, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.StreamDeleted, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task read_stream_events_forward_should_return_stream_deleted() {
		var result = await ReadIndex.ReadStreamEventsForward("ES", 0, 100, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.StreamDeleted, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task read_stream_events_backward_should_return_stream_deleted() {
		var result = await ReadIndex.ReadStreamEventsBackward("ES", -1, 100, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.StreamDeleted, result.Result);
		Assert.AreEqual(0, result.Records.Length);
	}

	[Test]
	public async Task read_all_forward_should_return_all_stream_records_except_uncommitted() {
		var events = (await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 100, CancellationToken.None))
			.EventRecords()
			.Select(r => r.Event)
			.ToArray();
		Assert.AreEqual(1, events.Length);
		Assert.AreEqual(_deleteTombstone, events[0]);
	}

	[Test]
	public async Task read_all_backward_should_return_all_stream_records_except_uncommitted() {
		var events = (await ReadIndex.ReadAllEventsBackward(GetBackwardReadPos(), 100, CancellationToken.None))
			.EventRecords()
			.Select(r => r.Event)
			.ToArray();
		Assert.AreEqual(1, events.Length);
		Assert.AreEqual(_deleteTombstone, events[0]);
	}
}
