// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.Transactions;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "Explicit transactions are not supported yet by Log V3")]
public class
	when_having_two_intermingled_transactions_spanning_few_chunks_read_index_should<TLogFormat, TStreamId>
	: ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord _p1;
	private EventRecord _p2;
	private EventRecord _p3;
	private EventRecord _p4;
	private EventRecord _p5;

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

		await WriteTransactionEnd(t2.CorrelationId, t2.TransactionPosition, streamId2, token: token);
		await WriteTransactionEnd(t1.CorrelationId, t1.TransactionPosition, streamId1, token: token);

		_t2CommitPos = await WriteCommit(t2.CorrelationId, t2.TransactionPosition, streamId2, _p2.EventNumber, token);
		_t1CommitPos = await WriteCommit(t1.CorrelationId, t1.TransactionPosition, streamId1, _p1.EventNumber, token);
	}

	[Test]
	public async Task return_correct_last_event_version_for_larger_stream() {
		Assert.AreEqual(2, await ReadIndex.GetStreamLastEventNumber("ES", CancellationToken.None));
	}

	[Test]
	public async Task return_correct_first_record_for_larger_stream() {
		var result = await ReadIndex.ReadEvent("ES", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_p1, result.Record);
	}

	[Test]
	public async Task return_correct_second_record_for_larger_stream() {
		var result = await ReadIndex.ReadEvent("ES", 1, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_p3, result.Record);
	}

	[Test]
	public async Task return_correct_third_record_for_larger_stream() {
		var result = await ReadIndex.ReadEvent("ES", 2, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_p5, result.Record);
	}

	[Test]
	public async Task not_find_record_with_nonexistent_version_for_larger_stream() {
		var result = await ReadIndex.ReadEvent("ES", 3, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_correct_range_on_from_start_range_query_for_larger_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("ES", 0, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(3, result.Records.Length);
		Assert.AreEqual(_p1, result.Records[0]);
		Assert.AreEqual(_p3, result.Records[1]);
		Assert.AreEqual(_p5, result.Records[2]);
	}

	[Test]
	public async Task return_correct_range_on_from_end_range_query_for_larger_stream_with_specific_version() {
		var result = await ReadIndex.ReadStreamEventsBackward("ES", 2, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(3, result.Records.Length);
		Assert.AreEqual(_p5, result.Records[0]);
		Assert.AreEqual(_p3, result.Records[1]);
		Assert.AreEqual(_p1, result.Records[2]);
	}

	[Test]
	public async Task return_correct_range_on_from_end_range_query_for_larger_stream_with_from_end_version() {
		var result = await ReadIndex.ReadStreamEventsBackward("ES", -1, 3, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(3, result.Records.Length);
		Assert.AreEqual(_p5, result.Records[0]);
		Assert.AreEqual(_p3, result.Records[1]);
		Assert.AreEqual(_p1, result.Records[2]);
	}

	[Test]
	public async Task return_correct_last_event_version_for_smaller_stream() {
		Assert.AreEqual(1, await ReadIndex.GetStreamLastEventNumber("ABC", CancellationToken.None));
	}

	[Test]
	public async Task return_correct_first_record_for_smaller_stream() {
		var result = await ReadIndex.ReadEvent("ABC", 0, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_p2, result.Record);
	}

	[Test]
	public async Task return_correct_second_record_for_smaller_stream() {
		var result = await ReadIndex.ReadEvent("ABC", 1, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_p4, result.Record);
	}

	[Test]
	public async Task not_find_record_with_nonexistent_version_for_smaller_stream() {
		var result = await ReadIndex.ReadEvent("ABC", 2, CancellationToken.None);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public async Task return_correct_range_on_from_start_range_query_for_smaller_stream() {
		var result = await ReadIndex.ReadStreamEventsForward("ABC", 0, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_p2, result.Records[0]);
		Assert.AreEqual(_p4, result.Records[1]);
	}

	[Test]
	public async Task return_correct_range_on_from_end_range_query_for_smaller_stream_with_specific_version() {
		var result = await ReadIndex.ReadStreamEventsBackward("ABC", 1, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_p4, result.Records[0]);
		Assert.AreEqual(_p2, result.Records[1]);
	}

	[Test]
	public async Task return_correct_range_on_from_end_range_query_for_smaller_stream_with_from_end_version() {
		var result = await ReadIndex.ReadStreamEventsBackward("ABC", -1, 2, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_p4, result.Records[0]);
		Assert.AreEqual(_p2, result.Records[1]);
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
		var records = (await ReadIndex.ReadAllEventsBackward(new TFPos(_t2CommitPos, 0), 10, CancellationToken.None)).Records;
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
		var pos = new TFPos(Db.Config.WriterCheckpoint.Read(), _p4.LogPosition); // p3 post-pos
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
		var recs = new[] {_p2, _p4, _p1, _p3, _p5}; // in committed order

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
		var recs = new[] {_p5, _p3, _p1, _p4, _p2}; // in reverse committed order

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
		var recs = new[] {_p2, _p4, _p1, _p3, _p5}; // in committed order

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
		var recs = new[] {_p5, _p3, _p1, _p4, _p2}; // in reverse committed order

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
}
