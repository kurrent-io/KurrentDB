// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.Scavenge;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "Explicit transactions are not supported yet by Log V3")]
public class when_scavenging_tfchunk_with_transactions<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private const string _streamIdOne = "ES-1";
	private const string _streamIdTwo = "ES-2";
	private EventRecord _p1, _p2, _p3, _p4, _p5, _random1;
	private long _t2CommitPos, _t1CommitPos, _postCommitPos;

	protected override void WriteTestScenario() {
		var t1 = WriteTransactionBegin(_streamIdOne, ExpectedVersion.NoStream);
		var t2 = WriteTransactionBegin(_streamIdTwo, ExpectedVersion.NoStream);

		_p1 = WriteTransactionEvent(t1.CorrelationId, t1.LogPosition, 0,
			_streamIdOne, 0, "es1", PrepareFlags.Data);
		_p2 = WriteTransactionEvent(t2.CorrelationId, t2.LogPosition, 0,
			_streamIdTwo, 0, "abc1", PrepareFlags.Data);
		_p3 = WriteTransactionEvent(t1.CorrelationId, t1.LogPosition, 1,
			_streamIdOne, 1, "es1", PrepareFlags.Data);
		_p4 = WriteTransactionEvent(t2.CorrelationId, t2.LogPosition, 1,
			_streamIdTwo, 1, "abc1", PrepareFlags.Data);
		_p5 = WriteTransactionEvent(t1.CorrelationId, t1.LogPosition, 2,
			_streamIdOne, 2, "es1", PrepareFlags.Data);

		WriteTransactionEnd(t2.CorrelationId, t2.TransactionPosition, _streamIdTwo);
		WriteTransactionEnd(t1.CorrelationId, t1.TransactionPosition, _streamIdOne);

		var t2Commit = WriteCommit(t2.TransactionPosition, _streamIdTwo, 0);
		_t2CommitPos = t2Commit.LogPosition;
		var t1Commit = WriteCommit(t1.TransactionPosition, _streamIdOne, 0);
		_t1CommitPos = t1Commit.LogPosition;
		_postCommitPos =
			t1Commit.GetNextLogPosition(t1Commit.LogPosition, t1Commit.GetSizeWithLengthPrefixAndSuffix() - 2 * sizeof(int));

		Writer.CompleteChunk();
		Writer.AddNewChunk();

		// Need to have a second chunk as otherwise the checkpoints will be off
		_random1 = WriteSingleEvent("random-stream", 0, "bla");

		Scavenge(completeLast: false, mergeChunks: true);
	}

	[Test]
	public async Task the_log_records_are_in_first_chunk() {
		var chunk = Db.Manager.GetChunk(0);

		var chunkRecords = new List<ILogRecord>();
		RecordReadResult result = await chunk.TryReadFirst(CancellationToken.None);
		while (result.Success) {
			chunkRecords.Add(result.LogRecord);
			result = chunk.TryReadClosestForward(result.NextPosition);
		}

		Assert.AreEqual(7, chunkRecords.Count);
	}

	[Test]
	public async Task the_log_records_are_unchanged_in_second_chunk() {
		var chunk = Db.Manager.GetChunk(1);

		var chunkRecords = new List<ILogRecord>();
		RecordReadResult result = await chunk.TryReadFirst(CancellationToken.None);
		while (result.Success) {
			chunkRecords.Add(result.LogRecord);
			result = chunk.TryReadClosestForward(result.NextPosition);
		}

		Assert.AreEqual(2, chunkRecords.Count);
	}

	public void return_correct_last_event_version_for_larger_stream() {
		Assert.AreEqual(2, ReadIndex.GetStreamLastEventNumber(_streamIdOne));
	}

	[Test]
	public void return_correct_first_record_for_larger_stream() {
		var result = ReadIndex.ReadEvent(_streamIdOne, 0);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_p1.EventId, result.Record.EventId);
	}

	[Test]
	public void return_correct_second_record_for_larger_stream() {
		var result = ReadIndex.ReadEvent(_streamIdOne, 1);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_p3.EventId, result.Record.EventId);
	}

	[Test]
	public void return_correct_third_record_for_larger_stream() {
		var result = ReadIndex.ReadEvent(_streamIdOne, 2);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_p5.EventId, result.Record.EventId);
	}

	[Test]
	public void not_find_record_with_nonexistent_version_for_larger_stream() {
		var result = ReadIndex.ReadEvent(_streamIdOne, 3);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public void return_correct_range_on_from_start_range_query_for_larger_stream() {
		var result = ReadIndex.ReadStreamEventsForward(_streamIdOne, 0, 3);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(3, result.Records.Length);
		Assert.AreEqual(_p1.EventId, result.Records[0].EventId);
		Assert.AreEqual(_p3.EventId, result.Records[1].EventId);
		Assert.AreEqual(_p5.EventId, result.Records[2].EventId);
	}

	[Test]
	public void return_correct_range_on_from_end_range_query_for_larger_stream_with_specific_version() {
		var result = ReadIndex.ReadStreamEventsBackward(_streamIdOne, 2, 3);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(3, result.Records.Length);
		Assert.AreEqual(_p5.EventId, result.Records[0].EventId);
		Assert.AreEqual(_p3.EventId, result.Records[1].EventId);
		Assert.AreEqual(_p1.EventId, result.Records[2].EventId);
	}

	[Test]
	public void return_correct_range_on_from_end_range_query_for_larger_stream_with_from_end_version() {
		var result = ReadIndex.ReadStreamEventsBackward(_streamIdOne, -1, 3);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(3, result.Records.Length);
		Assert.AreEqual(_p5.EventId, result.Records[0].EventId);
		Assert.AreEqual(_p3.EventId, result.Records[1].EventId);
		Assert.AreEqual(_p1.EventId, result.Records[2].EventId);
	}

	[Test]
	public void return_correct_last_event_version_for_smaller_stream() {
		Assert.AreEqual(1, ReadIndex.GetStreamLastEventNumber(_streamIdTwo));
	}

	[Test]
	public void return_correct_first_record_for_smaller_stream() {
		var result = ReadIndex.ReadEvent(_streamIdTwo, 0);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_p2.EventId, result.Record.EventId);
	}

	[Test]
	public void return_correct_second_record_for_smaller_stream() {
		var result = ReadIndex.ReadEvent(_streamIdTwo, 1);
		Assert.AreEqual(ReadEventResult.Success, result.Result);
		Assert.AreEqual(_p4.EventId, result.Record.EventId);
	}

	[Test]
	public void not_find_record_with_nonexistent_version_for_smaller_stream() {
		var result = ReadIndex.ReadEvent(_streamIdTwo, 2);
		Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		Assert.IsNull(result.Record);
	}

	[Test]
	public void return_correct_range_on_from_start_range_query_for_smaller_stream() {
		var result = ReadIndex.ReadStreamEventsForward(_streamIdTwo, 0, 2);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_p2.EventId, result.Records[0].EventId);
		Assert.AreEqual(_p4.EventId, result.Records[1].EventId);
	}

	[Test]
	public void return_correct_range_on_from_end_range_query_for_smaller_stream_with_specific_version() {
		var result = ReadIndex.ReadStreamEventsBackward(_streamIdTwo, 1, 2);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_p4.EventId, result.Records[0].EventId);
		Assert.AreEqual(_p2.EventId, result.Records[1].EventId);
	}

	[Test]
	public void return_correct_range_on_from_end_range_query_for_smaller_stream_with_from_end_version() {
		var result = ReadIndex.ReadStreamEventsBackward(_streamIdTwo, -1, 2);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_p4.EventId, result.Records[0].EventId);
		Assert.AreEqual(_p2.EventId, result.Records[1].EventId);
	}

	[Test]
	public void read_all_events_forward_returns_all_events_in_correct_order() {
		var records = ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 10).Records;
		Assert.AreEqual(6, records.Count);
		Assert.AreEqual(_p2.EventId, records[0].Event.EventId);
		Assert.AreEqual(_p4.EventId, records[1].Event.EventId);
		Assert.AreEqual(_p1.EventId, records[2].Event.EventId);
		Assert.AreEqual(_p3.EventId, records[3].Event.EventId);
		Assert.AreEqual(_p5.EventId, records[4].Event.EventId);
		Assert.AreEqual(_random1.EventId, records[5].Event.EventId);
	}

	[Test]
	public void read_all_events_backward_returns_all_events_in_correct_order() {
		var pos = GetBackwardReadPos();
		var records = ReadIndex.ReadAllEventsBackward(pos, 10).Records;

		Assert.AreEqual(6, records.Count);
		Assert.AreEqual(_random1.EventId, records[0].Event.EventId);
		Assert.AreEqual(_p5.EventId, records[1].Event.EventId);
		Assert.AreEqual(_p3.EventId, records[2].Event.EventId);
		Assert.AreEqual(_p1.EventId, records[3].Event.EventId);
		Assert.AreEqual(_p4.EventId, records[4].Event.EventId);
		Assert.AreEqual(_p2.EventId, records[5].Event.EventId);
	}

	[Test]
	public void
		read_all_events_forward_returns_no_transaction_records_when_prepare_position_is_greater_than_last_prepare_in_commit() {
		var records = ReadIndex.ReadAllEventsForward(new TFPos(_t1CommitPos, _t1CommitPos), 10).Records;
		Assert.AreEqual(1, records.Count);
		Assert.AreEqual(_random1.EventId, records[0].Event.EventId);
	}

	[Test]
	public void
		read_all_events_backwards_returns_nothing_when_prepare_position_is_smaller_than_first_prepare_in_commit() {
		var records = ReadIndex.ReadAllEventsBackward(new TFPos(_t2CommitPos, 0), 10).Records;
		Assert.AreEqual(0, records.Count);
	}

	[Test]
	public void read_all_events_forward_returns_correct_events_starting_in_the_middle_of_tf() {
		var res1 = ReadIndex.ReadAllEventsForward(new TFPos(_t2CommitPos, _p4.LogPosition),
			10); // end of first commit
		Assert.AreEqual(5, res1.Records.Count);
		Assert.AreEqual(_p4.EventId, res1.Records[0].Event.EventId);
		Assert.AreEqual(_p1.EventId, res1.Records[1].Event.EventId);
		Assert.AreEqual(_p3.EventId, res1.Records[2].Event.EventId);
		Assert.AreEqual(_p5.EventId, res1.Records[3].Event.EventId);
		Assert.AreEqual(_random1.EventId, res1.Records[4].Event.EventId);

		var res2 = ReadIndex.ReadAllEventsBackward(res1.PrevPos, 10);
		Assert.AreEqual(1, res2.Records.Count);
		Assert.AreEqual(_p2.EventId, res2.Records[0].Event.EventId);
	}

	[Test]
	public void read_all_events_backward_returns_correct_events_starting_in_the_middle_of_tf() {
		var pos = new TFPos(_postCommitPos, _p4.LogPosition); // p3 post position
		var res1 = ReadIndex.ReadAllEventsBackward(pos, 10);

		Assert.AreEqual(4, res1.Records.Count);
		Assert.AreEqual(_p3.EventId, res1.Records[0].Event.EventId);
		Assert.AreEqual(_p1.EventId, res1.Records[1].Event.EventId);
		Assert.AreEqual(_p4.EventId, res1.Records[2].Event.EventId);
		Assert.AreEqual(_p2.EventId, res1.Records[3].Event.EventId);

		var res2 = ReadIndex.ReadAllEventsForward(res1.PrevPos, 10);
		Assert.AreEqual(2, res2.Records.Count);
		Assert.AreEqual(_p5.EventId, res2.Records[0].Event.EventId);
	}

	[Test]
	public void all_records_can_be_read_sequentially_page_by_page_in_forward_pass() {
		var recs = new[] { _p2, _p4, _p1, _p3, _p5, _random1 }; // in committed order

		int count = 0;
		var pos = new TFPos(0, 0);
		IndexReadAllResult result;
		while ((result = ReadIndex.ReadAllEventsForward(pos, 1)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count].EventId, result.Records[0].Event.EventId);
			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}

	[Test]
	public void all_records_can_be_read_sequentially_page_by_page_in_backward_pass() {
		var recs = new[] { _random1, _p5, _p3, _p1, _p4, _p2 }; // in reverse committed order

		int count = 0;
		var pos = GetBackwardReadPos();
		IndexReadAllResult result;
		while ((result = ReadIndex.ReadAllEventsBackward(pos, 1)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count].EventId, result.Records[0].Event.EventId);
			pos = result.NextPos;
			count += 1;
		}

		Assert.AreEqual(recs.Length, count);
	}

	[Test]
	public void position_returned_for_prev_page_when_traversing_forward_allow_to_traverse_backward_correctly() {
		var recs = new[] { _p2, _p4, _p1, _p3, _p5, _random1 }; // in committed order

		int count = 0;
		var pos = new TFPos(0, 0);
		IndexReadAllResult result;
		while ((result = ReadIndex.ReadAllEventsForward(pos, 1)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count].EventId, result.Records[0].Event.EventId);

			var localPos = result.PrevPos;
			int localCount = 0;
			IndexReadAllResult localResult;
			while ((localResult = ReadIndex.ReadAllEventsBackward(localPos, 1)).Records.Count != 0) {
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
	public void position_returned_for_prev_page_when_traversing_backward_allow_to_traverse_forward_correctly() {
		var recs = new[] { _random1, _p5, _p3, _p1, _p4, _p2 }; // in reverse committed order

		int count = 0;
		var pos = GetBackwardReadPos();
		IndexReadAllResult result;
		while ((result = ReadIndex.ReadAllEventsBackward(pos, 1)).Records.Count != 0) {
			Assert.AreEqual(1, result.Records.Count);
			Assert.AreEqual(recs[count].EventId, result.Records[0].Event.EventId);

			var localPos = result.PrevPos;
			int localCount = 0;
			IndexReadAllResult localResult;
			while ((localResult = ReadIndex.ReadAllEventsForward(localPos, 1)).Records.Count != 0) {
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
