// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Tests.Services.Storage;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.LogCommon;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.Scavenge;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "No such thing as a V0 prepare in LogV3")]
public class when_scavenging_tfchunk_with_version0_log_records_using_transactions<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {

	private const string _streamIdOne = "ES-1";
	private const string _streamIdTwo = "ES-2";
	private PrepareLogRecord _p1, _p2, _p3, _p4, _p5, _random1;
	private long _t2CommitPos, _t1CommitPos, _postCommitPos;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		var t1 = await WriteTransactionBeginV0(Guid.NewGuid(), Writer.Position, _streamIdOne,
			ExpectedVersion.NoStream, token);
		var t2 = await WriteTransactionBeginV0(Guid.NewGuid(), Writer.Position, _streamIdTwo,
			ExpectedVersion.NoStream, token);

		_p1 = await WriteTransactionEventV0(t1.CorrelationId, Writer.Position, t1.LogPosition, 0,
			t1.EventStreamId, 0, "es1", PrepareFlags.Data, token);
		_p2 = await WriteTransactionEventV0(t2.CorrelationId, Writer.Position, t2.LogPosition, 0,
			t2.EventStreamId, 0, "abc1", PrepareFlags.Data, token);
		_p3 = await WriteTransactionEventV0(t1.CorrelationId, Writer.Position, t1.LogPosition, 1,
			t1.EventStreamId, 1, "es1", PrepareFlags.Data, token);
		_p4 = await WriteTransactionEventV0(t2.CorrelationId, Writer.Position, t2.LogPosition, 1,
			t2.EventStreamId, 1, "abc1", PrepareFlags.Data, token);
		_p5 = await WriteTransactionEventV0(t1.CorrelationId, Writer.Position, t1.LogPosition, 2,
			t1.EventStreamId, 2, "es1", PrepareFlags.Data, token);

		await WriteTransactionEndV0(t2.CorrelationId, Writer.Position, t2.TransactionPosition,
			t2.EventStreamId, token);
		await WriteTransactionEndV0(t1.CorrelationId, Writer.Position, t1.TransactionPosition,
			t1.EventStreamId, token);

		(_t2CommitPos, _postCommitPos) = await WriteCommitV0(t2.CorrelationId, Writer.Position, t2.TransactionPosition,
			t2.EventStreamId, 0, token);
		(_t1CommitPos, _postCommitPos) = await WriteCommitV0(t1.CorrelationId, Writer.Position, t1.TransactionPosition,
			t1.EventStreamId, 0, token);

		await Writer.CompleteChunk(token);
		await Writer.AddNewChunk(token: token);

		// Need to have a second chunk as otherwise the checkpoints will be off
		_random1 = await WriteSingleEventWithLogVersion0(Guid.NewGuid(), "random-stream",
			Writer.Position, 0, token: token);

		Scavenge(completeLast: false, mergeChunks: true);
	}

	private async ValueTask<PrepareLogRecord> WriteTransactionBeginV0(Guid id, long logPosition, string eventStreamId,
		long expectedVersion, CancellationToken token) {
		var prepare = new PrepareLogRecord(logPosition, id, Guid.NewGuid(), logPosition, -1,
			eventStreamId, null, expectedVersion,
			DateTime.UtcNow, PrepareFlags.TransactionBegin, null, null, new byte[0], new byte[0],
			LogRecordVersion.LogRecordV0);

		Assert.IsTrue(await Writer.Write(prepare, token) is (true, _));
		return prepare;
	}

	private async ValueTask<PrepareLogRecord> WriteTransactionEventV0(Guid correlationId, long logPosition, long transactionPosition,
		int transactionOffset, string eventStreamId, long eventNumber, string eventData, PrepareFlags flags,
		CancellationToken token) {
		var prepare = new PrepareLogRecord(logPosition, correlationId, Guid.NewGuid(), transactionPosition,
			transactionOffset,
			eventStreamId, null, ExpectedVersion.Any, DateTime.UtcNow, flags,
			"testEventType", null, Encoding.UTF8.GetBytes(eventData), new byte[0],
			LogRecordVersion.LogRecordV0);

		await Writer.Write(prepare, token);
		return prepare;
	}

	private async ValueTask WriteTransactionEndV0(Guid correlationId, long logPosition, long transactionId,
		string eventStreamId, CancellationToken token) {
		var prepare = new PrepareLogRecord(logPosition, correlationId, Guid.NewGuid(), transactionId, -1,
			eventStreamId, null, ExpectedVersion.Any,
			DateTime.UtcNow, PrepareFlags.TransactionEnd, null, null, new byte[0], new byte[0],
			LogRecordVersion.LogRecordV0);

		await Writer.Write(prepare, token);
	}

	private async ValueTask<(long, long)> WriteCommitV0(Guid correlationId, long logPosition, long transactionPosition, string eventStreamId,
		long eventNumber, CancellationToken token) {
		var commit = new CommitLogRecord(logPosition, correlationId, transactionPosition, DateTime.UtcNow,
			eventNumber,
			LogRecordVersion.LogRecordV0);

		var (_, pos) = await Writer.Write(commit, token);
		return (commit.LogPosition, pos);
	}

	[Test]
	public async Task the_log_records_are_still_version_0_in_first_chunk() {
		var chunk = await Db.Manager.GetInitializedChunk(0, CancellationToken.None);

		var chunkRecords = new List<ILogRecord>();
		RecordReadResult result = await chunk.TryReadFirst(CancellationToken.None);
		while (result.Success) {
			chunkRecords.Add(result.LogRecord);
			result = await chunk.TryReadClosestForward(result.NextPosition, CancellationToken.None);
		}

		Assert.IsTrue(chunkRecords.All(x => x.Version == LogRecordVersion.LogRecordV0));
		Assert.AreEqual(7, chunkRecords.Count);
	}

	[Test]
	public async Task the_log_records_are_unchanged_in_second_chunk() {
		var chunk = await Db.Manager.GetInitializedChunk(1, CancellationToken.None);

		var chunkRecords = new List<ILogRecord>();
		RecordReadResult result = await chunk.TryReadFirst(CancellationToken.None);
		while (result.Success) {
			chunkRecords.Add(result.LogRecord);
			result = await chunk.TryReadClosestForward(result.NextPosition, CancellationToken.None);
		}

		Assert.IsTrue(chunkRecords.All(x => x.Version == LogRecordVersion.LogRecordV0));
		Assert.AreEqual(2, chunkRecords.Count);
	}

	public async Task return_correct_last_event_version_for_larger_stream() {
		Assert.AreEqual(2, (await ReadIndex.GetStreamLastEventNumber(_streamIdOne, CancellationToken.None)));
	}

	[Test]
	public async Task return_correct_first_record_for_larger_stream() {
		var result = await ReadIndex.ReadEvent(_streamIdOne, 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_p1.EventId, result.Record.EventId);
	}

	[Test]
	public async Task return_correct_second_record_for_larger_stream() {
		var result = await ReadIndex.ReadEvent(_streamIdOne, 1, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_p3.EventId, result.Record.EventId);
	}

	[Test]
	public async Task return_correct_third_record_for_larger_stream() {
		var result = await ReadIndex.ReadEvent(_streamIdOne, 2, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_p5.EventId, result.Record.EventId);
	}

	[Test]
	public async Task not_find_record_with_nonexistent_version_for_larger_stream() {
		var result = await ReadIndex.ReadEvent(_streamIdOne, 3, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_correct_range_on_from_start_range_query_for_larger_stream() {
		var result = await ReadIndex.ReadStreamEventsForward(_streamIdOne, 0, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(3, result.Records.Length);
		Assert.AreEqual(_p1.EventId, result.Records[0].EventId);
		Assert.AreEqual(_p3.EventId, result.Records[1].EventId);
		Assert.AreEqual(_p5.EventId, result.Records[2].EventId);
	}

	[Test]
	public async Task return_correct_range_on_from_end_range_query_for_larger_stream_with_specific_version() {
		var result = await ReadIndex.ReadStreamEventsBackward(_streamIdOne, 2, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(3, result.Records.Length);
		Assert.AreEqual(_p5.EventId, result.Records[0].EventId);
		Assert.AreEqual(_p3.EventId, result.Records[1].EventId);
		Assert.AreEqual(_p1.EventId, result.Records[2].EventId);
	}

	[Test]
	public async Task return_correct_range_on_from_end_range_query_for_larger_stream_with_from_end_version() {
		var result = await ReadIndex.ReadStreamEventsBackward(_streamIdOne, -1, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(3, result.Records.Length);
		Assert.AreEqual(_p5.EventId, result.Records[0].EventId);
		Assert.AreEqual(_p3.EventId, result.Records[1].EventId);
		Assert.AreEqual(_p1.EventId, result.Records[2].EventId);
	}

	[Test]
	public async Task return_correct_last_event_version_for_smaller_stream() {
		Assert.AreEqual(1, await ReadIndex.GetStreamLastEventNumber(_streamIdTwo, CancellationToken.None));
	}

	[Test]
	public async Task return_correct_first_record_for_smaller_stream() {
		var result = await ReadIndex.ReadEvent(_streamIdTwo, 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_p2.EventId, result.Record.EventId);
	}

	[Test]
	public async Task return_correct_second_record_for_smaller_stream() {
		var result = await ReadIndex.ReadEvent(_streamIdTwo, 1, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_p4.EventId, result.Record.EventId);
	}

	[Test]
	public async Task not_find_record_with_nonexistent_version_for_smaller_stream() {
		var result = await ReadIndex.ReadEvent(_streamIdTwo, 2, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_correct_range_on_from_start_range_query_for_smaller_stream() {
		var result = await ReadIndex.ReadStreamEventsForward(_streamIdTwo, 0, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_p2.EventId, result.Records[0].EventId);
		Assert.AreEqual(_p4.EventId, result.Records[1].EventId);
	}

	[Test]
	public async Task return_correct_range_on_from_end_range_query_for_smaller_stream_with_specific_version() {
		var result = await ReadIndex.ReadStreamEventsBackward(_streamIdTwo, 1, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_p4.EventId, result.Records[0].EventId);
		Assert.AreEqual(_p2.EventId, result.Records[1].EventId);
	}

	[Test]
	public async Task return_correct_range_on_from_end_range_query_for_smaller_stream_with_from_end_version() {
		var result = await ReadIndex.ReadStreamEventsBackward(_streamIdTwo, -1, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_p4.EventId, result.Records[0].EventId);
		Assert.AreEqual(_p2.EventId, result.Records[1].EventId);
	}

	[Test]
	public async Task read_all_events_forward_returns_all_events_in_correct_order() {
		var records = (await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 10, CancellationToken.None)).Records;
		Assert.AreEqual(6, records.Count);
		Assert.AreEqual(_p2.EventId, records[0].Event.EventId);
		Assert.AreEqual(_p4.EventId, records[1].Event.EventId);
		Assert.AreEqual(_p1.EventId, records[2].Event.EventId);
		Assert.AreEqual(_p3.EventId, records[3].Event.EventId);
		Assert.AreEqual(_p5.EventId, records[4].Event.EventId);
		Assert.AreEqual(_random1.EventId, records[5].Event.EventId);
	}

	[Test]
	public async Task read_all_events_backward_returns_all_events_in_correct_order() {
		var pos = GetBackwardReadPos();
		var records = (await ReadIndex.ReadAllEventsBackward(pos, 10, CancellationToken.None)).Records;

		Assert.AreEqual(6, records.Count);
		Assert.AreEqual(_random1.EventId, records[0].Event.EventId);
		Assert.AreEqual(_p5.EventId, records[1].Event.EventId);
		Assert.AreEqual(_p3.EventId, records[2].Event.EventId);
		Assert.AreEqual(_p1.EventId, records[3].Event.EventId);
		Assert.AreEqual(_p4.EventId, records[4].Event.EventId);
		Assert.AreEqual(_p2.EventId, records[5].Event.EventId);
	}

	[Test]
	public async Task
		read_all_events_forward_returns_no_transaction_records_when_prepare_position_is_greater_than_last_prepare_in_commit() {
		var records = (await ReadIndex.ReadAllEventsForward(new TFPos(_t1CommitPos, _t1CommitPos), 10, CancellationToken.None))
			.Records;
		Assert.AreEqual(1, records.Count);
		Assert.AreEqual(_random1.EventId, records[0].Event.EventId);
	}

	[Test]
	public async Task
		read_all_events_backwards_returns_nothing_when_prepare_position_is_smaller_than_first_prepare_in_commit() {
		var records =
			(await ReadIndex.ReadAllEventsBackward(new TFPos(_t2CommitPos, 0), 10, CancellationToken.None)).Records;
		Assert.AreEqual(0, records.Count);
	}

	[Test]
	public async Task read_all_events_forward_returns_correct_events_starting_in_the_middle_of_tf() {
		var res1 = await ReadIndex.ReadAllEventsForward(new TFPos(_t2CommitPos, _p4.LogPosition),
			10, CancellationToken.None); // end of first commit
		Assert.AreEqual(5, res1.Records.Count);
		Assert.AreEqual(_p4.EventId, res1.Records[0].Event.EventId);
		Assert.AreEqual(_p1.EventId, res1.Records[1].Event.EventId);
		Assert.AreEqual(_p3.EventId, res1.Records[2].Event.EventId);
		Assert.AreEqual(_p5.EventId, res1.Records[3].Event.EventId);
		Assert.AreEqual(_random1.EventId, res1.Records[4].Event.EventId);

		var res2 = await ReadIndex.ReadAllEventsBackward(res1.PrevPos, 10, CancellationToken.None);
		Assert.AreEqual(1, res2.Records.Count);
		Assert.AreEqual(_p2.EventId, res2.Records[0].Event.EventId);
	}

	[Test]
	public async Task read_all_events_backward_returns_correct_events_starting_in_the_middle_of_tf() {
		var pos = new TFPos(_postCommitPos, _p4.LogPosition); // p3 post position
		var res1 = await ReadIndex.ReadAllEventsBackward(pos, 10, CancellationToken.None);

		Assert.AreEqual(4, res1.Records.Count);
		Assert.AreEqual(_p3.EventId, res1.Records[0].Event.EventId);
		Assert.AreEqual(_p1.EventId, res1.Records[1].Event.EventId);
		Assert.AreEqual(_p4.EventId, res1.Records[2].Event.EventId);
		Assert.AreEqual(_p2.EventId, res1.Records[3].Event.EventId);

		var res2 = await ReadIndex.ReadAllEventsForward(res1.PrevPos, 10, CancellationToken.None);
		Assert.AreEqual(2, res2.Records.Count);
		Assert.AreEqual(_p5.EventId, res2.Records[0].Event.EventId);
	}

	[Test]
	public async Task all_records_can_be_read_sequentially_page_by_page_in_forward_pass() {
		var recs = new[] {_p2, _p4, _p1, _p3, _p5, _random1}; // in committed order

		int count = 0;
		var pos = new TFPos(0, 0);
		IndexReadAllResult result;
		while ((result = await ReadIndex.ReadAllEventsForward(pos, 1, CancellationToken.None)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count].EventId, result.Records[0].Event.EventId);
			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}

	[Test]
	public async Task all_records_can_be_read_sequentially_page_by_page_in_backward_pass() {
		var recs = new[] {_random1, _p5, _p3, _p1, _p4, _p2}; // in reverse committed order

		int count = 0;
		var pos = GetBackwardReadPos();
		IndexReadAllResult result;
		while ((result = await ReadIndex.ReadAllEventsBackward(pos, 1, CancellationToken.None)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count].EventId, result.Records[0].Event.EventId);
			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}

	[Test]
	public async Task position_returned_for_prev_page_when_traversing_forward_allow_to_traverse_backward_correctly() {
		var recs = new[] {_p2, _p4, _p1, _p3, _p5, _random1}; // in committed order

		int count = 0;
		var pos = new TFPos(0, 0);
		IndexReadAllResult result;
		while ((result = await ReadIndex.ReadAllEventsForward(pos, 1, CancellationToken.None)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count].EventId, result.Records[0].Event.EventId);

			var localPos = result.PrevPos;
			int localCount = 0;
			IndexReadAllResult localResult;
			while ((localResult = await ReadIndex.ReadAllEventsBackward(localPos, 1, CancellationToken.None)).Records.Count != 0) {
				Assert.AreEqual(1, localResult.Records.Count);
				Assert.AreEqual(recs[count - 1 - localCount].EventId, localResult.Records[0].Event.EventId);
				localPos = localResult.NextPos;
				localCount += 1;
			}

			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}

	[Test]
	public async Task position_returned_for_prev_page_when_traversing_backward_allow_to_traverse_forward_correctly() {
		var recs = new[] {_random1, _p5, _p3, _p1, _p4, _p2}; // in reverse committed order

		int count = 0;
		var pos = GetBackwardReadPos();
		IndexReadAllResult result;
		while ((result = await ReadIndex.ReadAllEventsBackward(pos, 1, CancellationToken.None)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count].EventId, result.Records[0].Event.EventId);

			var localPos = result.PrevPos;
			int localCount = 0;
			IndexReadAllResult localResult;
			while ((localResult = await ReadIndex.ReadAllEventsForward(localPos, 1, CancellationToken.None)).Records.Count != 0) {
				Assert.AreEqual(1, localResult.Records.Count);
				Assert.AreEqual(recs[count - 1 - localCount].EventId, localResult.Records[0].Event.EventId);
				localPos = localResult.NextPos;
				localCount += 1;
			}

			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}
}
