// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.Storage.ReaderIndex;
using KurrentDB.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.Services.Storage.Transactions;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "Explicit transactions are not supported yet by Log V3")]
public class
	when_having_two_intermingled_transactions_and_some_uncommitted_prepares_spanning_few_chunks_read_index_should<TLogFormat, TStreamId> :
		ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord _p1;
	private EventRecord _p2;
	private EventRecord _p3;
	private EventRecord _p4;
	private EventRecord _p5;

	private long _pos6, _pos7, _pos8;

	private long _t1CommitPos;
	private long _t2CommitPos;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		const string streamId1 = "ES";
		const string streamId2 = "ABC";

		var t1 = await WriteTransactionBegin(streamId1, ExpectedVersion.NoStream, token);
		var t2 = await WriteTransactionBegin(streamId2, ExpectedVersion.NoStream, token);

		_p1 = await WriteTransactionEvent(t1.CorrelationId, t1.LogPosition, 0, streamId1, 0,
			"es1" + new string('.', 3000), PrepareFlags.Data, token: token);
		_p2 = await WriteTransactionEvent(t2.CorrelationId, t2.LogPosition, 0, streamId2, 0,
			"abc1" + new string('.', 3000), PrepareFlags.Data, token: token);
		_p3 = await WriteTransactionEvent(t1.CorrelationId, t1.LogPosition, 1, streamId1, 1,
			"es1" + new string('.', 3000), PrepareFlags.Data, token: token);
		_p4 = await WriteTransactionEvent(t2.CorrelationId, t2.LogPosition, 1, streamId2, 1,
			"abc1" + new string('.', 3000), PrepareFlags.Data, retryOnFail: true, token: token);
		_p5 = await WriteTransactionEvent(t1.CorrelationId, t1.LogPosition, 2, streamId1, 2,
			"es1" + new string('.', 3000), PrepareFlags.Data, token: token);

		await WriteTransactionEnd(t2.CorrelationId, t2.TransactionPosition, streamId2, token);
		await WriteTransactionEnd(t1.CorrelationId, t1.TransactionPosition, streamId1, token);

		_t2CommitPos = await WriteCommit(t2.CorrelationId, t2.TransactionPosition, streamId2, _p2.EventNumber, token: token);
		_t1CommitPos = await WriteCommit(t1.CorrelationId, t1.TransactionPosition, streamId1, _p1.EventNumber, token: token);

		var (t1StreamId, _) = await GetOrReserve("t1", token);
		(var eventTypeId, _pos6) = await GetOrReserveEventType("et", token);
		var r6 = LogRecord.Prepare(_recordFactory, _pos6, Guid.NewGuid(), Guid.NewGuid(), _pos6, 0, t1StreamId, -1,
			PrepareFlags.SingleWrite, eventTypeId, LogRecord.NoData, LogRecord.NoData);
		(_, _pos7) = await Writer.Write(r6, token);
		var r7 = LogRecord.Prepare(_recordFactory, _pos7, Guid.NewGuid(), Guid.NewGuid(), _pos7, 0, t1StreamId, -1,
			PrepareFlags.SingleWrite, eventTypeId, LogRecord.NoData, LogRecord.NoData);
		(_, _pos8) = await Writer.Write(r7, token);
		var r8 = LogRecord.Prepare(_recordFactory, _pos8, Guid.NewGuid(), Guid.NewGuid(), _pos8, 0, t1StreamId, -1,
			PrepareFlags.SingleWrite, eventTypeId, LogRecord.NoData, LogRecord.NoData);

		await Writer.Write(r8, token);
	}

	[Test]
	public async Task read_all_events_forward_returns_all_events_in_correct_order() {
		var records = (await ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 10, CancellationToken.None))
			.Records;

		Assert.AreEqual(5, records.Count);
		Assert.AreEqual(_p2, records[0].Event);
		Assert.AreEqual(_p4, records[1].Event);
		Assert.AreEqual(_p1, records[2].Event);
		Assert.AreEqual(_p3, records[3].Event);
		Assert.AreEqual(_p5, records[4].Event);
	}

	[Test]
	public async Task read_all_events_backward_returns_all_events_in_correct_order() {
		var pos = GetBackwardReadPos();
		var records = (await ReadIndex.ReadAllEventsBackward(pos, 10, CancellationToken.None)).Records;

		Assert.AreEqual(5, records.Count);
		Assert.AreEqual(_p5, records[0].Event);
		Assert.AreEqual(_p3, records[1].Event);
		Assert.AreEqual(_p1, records[2].Event);
		Assert.AreEqual(_p4, records[3].Event);
		Assert.AreEqual(_p2, records[4].Event);
	}

	[Test]
	public async Task
		read_all_events_forward_returns_nothing_when_prepare_position_is_greater_than_last_prepare_in_commit() {
		var records = (await ReadIndex.ReadAllEventsForward(new TFPos(_t1CommitPos, _t1CommitPos), 10, CancellationToken.None))
			.Records;
		Assert.AreEqual(0, records.Count);
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
		var res1 = await ReadIndex.ReadAllEventsForward(new TFPos(_t2CommitPos, _p4.LogPosition), 10, CancellationToken.None);

		Assert.AreEqual(4, res1.Records.Count);
		Assert.AreEqual(_p4, res1.Records[0].Event);
		Assert.AreEqual(_p1, res1.Records[1].Event);
		Assert.AreEqual(_p3, res1.Records[2].Event);
		Assert.AreEqual(_p5, res1.Records[3].Event);

		var res2 = await ReadIndex.ReadAllEventsBackward(res1.PrevPos, 10, CancellationToken.None);
		Assert.AreEqual(1, res2.Records.Count);
		Assert.AreEqual(_p2, res2.Records[0].Event);
	}

	[Test]
	public async Task read_all_events_backward_returns_correct_events_starting_in_the_middle_of_tf() {
		var pos = new TFPos(_pos6, _p4.LogPosition); // p3 post-pos
		var res1 = await ReadIndex.ReadAllEventsBackward(pos, 10, CancellationToken.None);

		Assert.AreEqual(4, res1.Records.Count);
		Assert.AreEqual(_p3, res1.Records[0].Event);
		Assert.AreEqual(_p1, res1.Records[1].Event);
		Assert.AreEqual(_p4, res1.Records[2].Event);
		Assert.AreEqual(_p2, res1.Records[3].Event);

		var res2 = await ReadIndex.ReadAllEventsForward(res1.PrevPos, 10, CancellationToken.None);
		Assert.AreEqual(1, res2.Records.Count);
		Assert.AreEqual(_p5, res2.Records[0].Event);
	}

	[Test]
	public async Task all_records_can_be_read_sequentially_page_by_page_in_forward_pass() {
		var recs = new[] { _p2, _p4, _p1, _p3, _p5 }; // in committed order

		int count = 0;
		var pos = new TFPos(0, 0);
		IndexReadAllResult result;
		while ((result = await ReadIndex.ReadAllEventsForward(pos, 1, CancellationToken.None)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count], result.Records[0].Event);
			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}

	[Test]
	public async Task all_records_can_be_read_sequentially_page_by_page_in_backward_pass() {
		var recs = new[] { _p5, _p3, _p1, _p4, _p2 }; // in reverse committed order

		int count = 0;
		var pos = GetBackwardReadPos();
		IndexReadAllResult result;
		while ((result = await ReadIndex.ReadAllEventsBackward(pos, 1, CancellationToken.None)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count], result.Records[0].Event);
			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}

	[Test]
	public async Task position_returned_for_prev_page_when_traversing_forward_allow_to_traverse_backward_correctly() {
		var recs = new[] { _p2, _p4, _p1, _p3, _p5 }; // in committed order

		int count = 0;
		var pos = new TFPos(0, 0);
		IndexReadAllResult result;
		while ((result = await ReadIndex.ReadAllEventsForward(pos, 1, CancellationToken.None)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count], result.Records[0].Event);

			var localPos = result.PrevPos;
			int localCount = 0;
			IndexReadAllResult localResult;
			while ((localResult = await ReadIndex.ReadAllEventsBackward(localPos, 1, CancellationToken.None)).Records.Count != 0) {
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
	public async Task position_returned_for_prev_page_when_traversing_backward_allow_to_traverse_forward_correctly() {
		var recs = new[] { _p5, _p3, _p1, _p4, _p2 }; // in reverse committed order

		int count = 0;
		var pos = GetBackwardReadPos();
		IndexReadAllResult result;
		while ((result = await ReadIndex.ReadAllEventsBackward(pos, 1, CancellationToken.None)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count], result.Records[0].Event);

			var localPos = result.PrevPos;
			int localCount = 0;
			IndexReadAllResult localResult;
			while ((localResult = await ReadIndex.ReadAllEventsForward(localPos, 1, CancellationToken.None)).Records.Count != 0) {
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
	public async Task
		reading_all_forward_at_position_with_no_commits_after_returns_prev_pos_that_allows_to_traverse_back() {
		var res1 = await ReadIndex.ReadAllEventsForward(new TFPos(_pos6, 0), 100, CancellationToken.None);
		Assert.AreEqual(0, res1.Records.Count);

		var recs = new[] { _p5, _p3, _p1, _p4, _p2 }; // in reverse committed order
		int count = 0;
		IndexReadAllResult result;
		TFPos pos = res1.PrevPos;
		while ((result = await ReadIndex.ReadAllEventsBackward(pos, 1, CancellationToken.None)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count], result.Records[0].Event);
			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}

	[Test]
	public async Task reading_all_forward_at_the_very_end_returns_prev_pos_that_allows_to_traverse_back() {
		var res1 = await ReadIndex.ReadAllEventsForward(new TFPos(Db.Config.WriterCheckpoint.Read(), 0), 100, CancellationToken.None);
		Assert.AreEqual(0, res1.Records.Count);

		var recs = new[] { _p5, _p3, _p1, _p4, _p2 }; // in reverse committed order
		int count = 0;
		IndexReadAllResult result;
		TFPos pos = res1.PrevPos;
		while ((result = await ReadIndex.ReadAllEventsBackward(pos, 1, CancellationToken.None)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count], result.Records[0].Event);
			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}

	[Test]
	public async Task
		reading_all_backward_at_position_with_no_commits_before_returns_prev_pos_that_allows_to_traverse_forward() {
		var res1 = await ReadIndex.ReadAllEventsBackward(new TFPos(_t2CommitPos, int.MaxValue), 100, CancellationToken.None);
		Assert.AreEqual(0, res1.Records.Count);

		var recs = new[] { _p2, _p4, _p1, _p3, _p5 };
		int count = 0;
		IndexReadAllResult result;
		TFPos pos = res1.PrevPos;
		while ((result = await ReadIndex.ReadAllEventsForward(pos, 1, CancellationToken.None)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count], result.Records[0].Event);
			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}

	[Test]
	public async Task reading_all_backward_at_the_very_beginning_returns_prev_pos_that_allows_to_traverse_forward() {
		var res1 = await ReadIndex.ReadAllEventsBackward(new TFPos(0, int.MaxValue), 100, CancellationToken.None);
		Assert.AreEqual(0, res1.Records.Count);

		var recs = new[] { _p2, _p4, _p1, _p3, _p5 };
		int count = 0;
		IndexReadAllResult result;
		TFPos pos = res1.PrevPos;
		while ((result = await ReadIndex.ReadAllEventsForward(pos, 1, CancellationToken.None)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count], result.Records[0].Event);
			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}
}
