// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.Transactions;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "Explicit transactions are not supported yet by Log V3")]
public class
	when_having_two_intermingled_transactions_and_some_uncommited_prepares_spanning_few_chunks_read_index_should<TLogFormat, TStreamId> :
		ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord _p1;
	private EventRecord _p2;
	private EventRecord _p3;
	private EventRecord _p4;
	private EventRecord _p5;

	private long _pos6, _pos7, _pos8;

	private long _t1CommitPos;
	private long _t2CommitPos;

	protected override void WriteTestScenario() {
		const string streamId1 = "ES";
		const string streamId2 = "ABC";

		var t1 = WriteTransactionBegin(streamId1, ExpectedVersion.NoStream);
		var t2 = WriteTransactionBegin(streamId2, ExpectedVersion.NoStream);

		_p1 = WriteTransactionEvent(t1.CorrelationId, t1.LogPosition, 0, streamId1, 0,
			"es1" + new string('.', 3000), PrepareFlags.Data);
		_p2 = WriteTransactionEvent(t2.CorrelationId, t2.LogPosition, 0, streamId2, 0,
			"abc1" + new string('.', 3000), PrepareFlags.Data);
		_p3 = WriteTransactionEvent(t1.CorrelationId, t1.LogPosition, 1, streamId1, 1,
			"es1" + new string('.', 3000), PrepareFlags.Data);
		_p4 = WriteTransactionEvent(t2.CorrelationId, t2.LogPosition, 1, streamId2, 1,
			"abc1" + new string('.', 3000), PrepareFlags.Data, retryOnFail: true);
		_p5 = WriteTransactionEvent(t1.CorrelationId, t1.LogPosition, 2, streamId1, 2,
			"es1" + new string('.', 3000), PrepareFlags.Data);

		WriteTransactionEnd(t2.CorrelationId, t2.TransactionPosition, streamId2);
		WriteTransactionEnd(t1.CorrelationId, t1.TransactionPosition, streamId1);

		_t2CommitPos = WriteCommit(t2.CorrelationId, t2.TransactionPosition, streamId2, _p2.EventNumber);
		_t1CommitPos = WriteCommit(t1.CorrelationId, t1.TransactionPosition, streamId1, _p1.EventNumber);

		GetOrReserve("t1", out var t1StreamId, out _pos6);
		GetOrReserveEventType("et", out var eventTypeId, out _pos6);
		var r6 = LogRecord.Prepare(_recordFactory, _pos6, Guid.NewGuid(), Guid.NewGuid(), _pos6, 0, t1StreamId, -1,
			PrepareFlags.SingleWrite, eventTypeId, LogRecord.NoData, LogRecord.NoData);
		Writer.Write(r6, out _pos7);
		var r7 = LogRecord.Prepare(_recordFactory, _pos7, Guid.NewGuid(), Guid.NewGuid(), _pos7, 0, t1StreamId, -1,
			PrepareFlags.SingleWrite, eventTypeId, LogRecord.NoData, LogRecord.NoData);
		Writer.Write(r7, out _pos8);
		var r8 = LogRecord.Prepare(_recordFactory, _pos8, Guid.NewGuid(), Guid.NewGuid(), _pos8, 0, t1StreamId, -1,
			PrepareFlags.SingleWrite, eventTypeId, LogRecord.NoData, LogRecord.NoData);
		long pos9;
		Writer.Write(r8, out pos9);
	}

	[Test]
	public void read_all_events_forward_returns_all_events_in_correct_order() {
		var records = ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 10).Records;

		Assert.AreEqual(5, records.Count);
		Assert.AreEqual(_p2, records[0].Event);
		Assert.AreEqual(_p4, records[1].Event);
		Assert.AreEqual(_p1, records[2].Event);
		Assert.AreEqual(_p3, records[3].Event);
		Assert.AreEqual(_p5, records[4].Event);
	}

	[Test]
	public void read_all_events_backward_returns_all_events_in_correct_order() {
		var pos = GetBackwardReadPos();
		var records = ReadIndex.ReadAllEventsBackward(pos, 10).Records;

		Assert.AreEqual(5, records.Count);
		Assert.AreEqual(_p5, records[0].Event);
		Assert.AreEqual(_p3, records[1].Event);
		Assert.AreEqual(_p1, records[2].Event);
		Assert.AreEqual(_p4, records[3].Event);
		Assert.AreEqual(_p2, records[4].Event);
	}

	[Test]
	public void
		read_all_events_forward_returns_nothing_when_prepare_position_is_greater_than_last_prepare_in_commit() {
		var records = ReadIndex.ReadAllEventsForward(new TFPos(_t1CommitPos, _t1CommitPos), 10).Records;
		Assert.AreEqual(0, records.Count);
	}

	[Test]
	public void
		read_all_events_backwards_returns_nothing_when_prepare_position_is_smaller_than_first_prepare_in_commit() {
		var records = ReadIndex.ReadAllEventsBackward(new TFPos(_t2CommitPos, 0), 10).Records;
		Assert.AreEqual(0, records.Count);
	}

	[Test]
	public void read_all_events_forward_returns_correct_events_starting_in_the_middle_of_tf() {
		var res1 = ReadIndex.ReadAllEventsForward(new TFPos(_t2CommitPos, _p4.LogPosition), 10);

		Assert.AreEqual(4, res1.Records.Count);
		Assert.AreEqual(_p4, res1.Records[0].Event);
		Assert.AreEqual(_p1, res1.Records[1].Event);
		Assert.AreEqual(_p3, res1.Records[2].Event);
		Assert.AreEqual(_p5, res1.Records[3].Event);

		var res2 = ReadIndex.ReadAllEventsBackward(res1.PrevPos, 10);
		Assert.AreEqual(1, res2.Records.Count);
		Assert.AreEqual(_p2, res2.Records[0].Event);
	}

	[Test]
	public void read_all_events_backward_returns_correct_events_starting_in_the_middle_of_tf() {
		var pos = new TFPos(_pos6, _p4.LogPosition); // p3 post-pos
		var res1 = ReadIndex.ReadAllEventsBackward(pos, 10);

		Assert.AreEqual(4, res1.Records.Count);
		Assert.AreEqual(_p3, res1.Records[0].Event);
		Assert.AreEqual(_p1, res1.Records[1].Event);
		Assert.AreEqual(_p4, res1.Records[2].Event);
		Assert.AreEqual(_p2, res1.Records[3].Event);

		var res2 = ReadIndex.ReadAllEventsForward(res1.PrevPos, 10);
		Assert.AreEqual(1, res2.Records.Count);
		Assert.AreEqual(_p5, res2.Records[0].Event);
	}

	[Test]
	public void all_records_can_be_read_sequentially_page_by_page_in_forward_pass() {
		var recs = new[] { _p2, _p4, _p1, _p3, _p5 }; // in committed order

		int count = 0;
		var pos = new TFPos(0, 0);
		IndexReadAllResult result;
		while ((result = ReadIndex.ReadAllEventsForward(pos, 1)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count], result.Records[0].Event);
			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}

	[Test]
	public void all_records_can_be_read_sequentially_page_by_page_in_backward_pass() {
		var recs = new[] { _p5, _p3, _p1, _p4, _p2 }; // in reverse committed order

		int count = 0;
		var pos = GetBackwardReadPos();
		IndexReadAllResult result;
		while ((result = ReadIndex.ReadAllEventsBackward(pos, 1)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count], result.Records[0].Event);
			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}

	[Test]
	public void position_returned_for_prev_page_when_traversing_forward_allow_to_traverse_backward_correctly() {
		var recs = new[] { _p2, _p4, _p1, _p3, _p5 }; // in committed order

		int count = 0;
		var pos = new TFPos(0, 0);
		IndexReadAllResult result;
		while ((result = ReadIndex.ReadAllEventsForward(pos, 1)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count], result.Records[0].Event);

			var localPos = result.PrevPos;
			int localCount = 0;
			IndexReadAllResult localResult;
			while ((localResult = ReadIndex.ReadAllEventsBackward(localPos, 1)).Records.Count != 0) {
				Assert.AreEqual(1, localResult.Records.Count);
				Assert.AreEqual(recs[count - 1 - localCount], localResult.Records[0].Event);
				localPos = localResult.NextPos;
				localCount += 1;
			}

			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}

	[Test]
	public void position_returned_for_prev_page_when_traversing_backward_allow_to_traverse_forward_correctly() {
		var recs = new[] { _p5, _p3, _p1, _p4, _p2 }; // in reverse committed order

		int count = 0;
		var pos = GetBackwardReadPos();
		IndexReadAllResult result;
		while ((result = ReadIndex.ReadAllEventsBackward(pos, 1)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count], result.Records[0].Event);

			var localPos = result.PrevPos;
			int localCount = 0;
			IndexReadAllResult localResult;
			while ((localResult = ReadIndex.ReadAllEventsForward(localPos, 1)).Records.Count != 0) {
				Assert.AreEqual(1, localResult.Records.Count);
				Assert.AreEqual(recs[count - 1 - localCount], localResult.Records[0].Event);
				localPos = localResult.NextPos;
				localCount += 1;
			}

			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}

	[Test]
	public void
		reading_all_forward_at_position_with_no_commits_after_returns_prev_pos_that_allows_to_traverse_back() {
		var res1 = ReadIndex.ReadAllEventsForward(new TFPos(_pos6, 0), 100);
		Assert.AreEqual(0, res1.Records.Count);

		var recs = new[] { _p5, _p3, _p1, _p4, _p2 }; // in reverse committed order
		int count = 0;
		IndexReadAllResult result;
		TFPos pos = res1.PrevPos;
		while ((result = ReadIndex.ReadAllEventsBackward(pos, 1)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count], result.Records[0].Event);
			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}

	[Test]
	public void reading_all_forward_at_the_very_end_returns_prev_pos_that_allows_to_traverse_back() {
		var res1 = ReadIndex.ReadAllEventsForward(new TFPos(Db.Config.WriterCheckpoint.Read(), 0), 100);
		Assert.AreEqual(0, res1.Records.Count);

		var recs = new[] { _p5, _p3, _p1, _p4, _p2 }; // in reverse committed order
		int count = 0;
		IndexReadAllResult result;
		TFPos pos = res1.PrevPos;
		while ((result = ReadIndex.ReadAllEventsBackward(pos, 1)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count], result.Records[0].Event);
			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}

	[Test]
	public void
		reading_all_backward_at_position_with_no_commits_before_returns_prev_pos_that_allows_to_traverse_forward() {
		var res1 = ReadIndex.ReadAllEventsBackward(new TFPos(_t2CommitPos, int.MaxValue), 100);
		Assert.AreEqual(0, res1.Records.Count);

		var recs = new[] { _p2, _p4, _p1, _p3, _p5 };
		int count = 0;
		IndexReadAllResult result;
		TFPos pos = res1.PrevPos;
		while ((result = ReadIndex.ReadAllEventsForward(pos, 1)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count], result.Records[0].Event);
			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}

	[Test]
	public void reading_all_backward_at_the_very_beginning_returns_prev_pos_that_allows_to_traverse_forward() {
		var res1 = ReadIndex.ReadAllEventsBackward(new TFPos(0, int.MaxValue), 100);
		Assert.AreEqual(0, res1.Records.Count);

		var recs = new[] { _p2, _p4, _p1, _p3, _p5 };
		int count = 0;
		IndexReadAllResult result;
		TFPos pos = res1.PrevPos;
		while ((result = ReadIndex.ReadAllEventsForward(pos, 1)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count], result.Records[0].Event);
			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}
}
