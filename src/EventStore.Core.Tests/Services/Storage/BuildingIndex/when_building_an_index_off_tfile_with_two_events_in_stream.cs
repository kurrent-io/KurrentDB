using System;
using EventStore.Core.Data;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.BuildingIndex {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_building_an_index_off_tfile_with_two_events_in_stream<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
		private Guid _id1;
		private Guid _id2;

		private IPrepareLogRecord<TStreamId> _prepare1;
		private IPrepareLogRecord<TStreamId> _prepare2;

		protected override void WriteTestScenario() {
			_id1 = Guid.NewGuid();
			_id2 = Guid.NewGuid();

			long pos0, pos1, pos2, pos3, pos4;
			GetOrReserve("test1", out var streamId1, out pos0);
			GetOrReserveEventType("eventType", out var eventTypeId, out pos0);
			_prepare1 = LogRecord.SingleWrite(_recordFactory, pos0, _id1, _id1, streamId1, ExpectedVersion.NoStream, eventTypeId, new byte[0], new byte[0]);
			Writer.Write(_prepare1, out pos1);
			_prepare2 = LogRecord.SingleWrite(_recordFactory, pos1, _id2, _id2, streamId1, 0, eventTypeId, new byte[0], new byte[0]);
			Writer.Write(_prepare2, out pos2);
			Writer.Write(new CommitLogRecord(pos2, _id1, pos0, DateTime.UtcNow, 0), out pos3);
			Writer.Write(new CommitLogRecord(pos3, _id2, pos1, DateTime.UtcNow, 1), out pos4);
		}

		[Test]
		public void the_first_event_can_be_read() {
			var result = ReadIndex.ReadEvent("test1", 0);
			Assert.AreEqual(ReadEventResult.Success, result.Result);
			Assert.AreEqual(new EventRecord(0, _prepare1, "test1", "eventType"), result.Record);
		}

		[Test]
		public void the_second_event_can_be_read() {
			var result = ReadIndex.ReadEvent("test1", 1);
			Assert.AreEqual(ReadEventResult.Success, result.Result);
			Assert.AreEqual(new EventRecord(1, _prepare2, "test1", "eventType"), result.Record);
		}

		[Test]
		public void the_nonexisting_event_can_not_be_read() {
			var result = ReadIndex.ReadEvent("test1", 2);
			Assert.AreEqual(ReadEventResult.NotFound, result.Result);
		}

		[Test]
		public void the_last_event_can_be_read_and_is_correct() {
			var result = ReadIndex.ReadEvent("test1", -1);
			Assert.AreEqual(ReadEventResult.Success, result.Result);
			Assert.AreEqual(new EventRecord(1, _prepare2, "test1", "eventType"), result.Record);
		}

		[Test]
		public void the_first_event_can_be_read_through_range_query() {
			var result = ReadIndex.ReadStreamEventsBackward("test1", 0, 1);
			Assert.AreEqual(ReadStreamResult.Success, result.Result);
			Assert.AreEqual(1, result.Records.Length);
			Assert.AreEqual(new EventRecord(0, _prepare1, "test1", "eventType"), result.Records[0]);
		}

		[Test]
		public void the_second_event_can_be_read_through_range_query() {
			var result = ReadIndex.ReadStreamEventsBackward("test1", 1, 1);
			Assert.AreEqual(ReadStreamResult.Success, result.Result);
			Assert.AreEqual(1, result.Records.Length);
			Assert.AreEqual(new EventRecord(1, _prepare2, "test1", "eventType"), result.Records[0]);
		}

		[Test]
		public void the_stream_can_be_read_as_a_whole_with_specific_from_version() {
			var result = ReadIndex.ReadStreamEventsBackward("test1", 1, 2);
			Assert.AreEqual(ReadStreamResult.Success, result.Result);
			Assert.AreEqual(2, result.Records.Length);
			Assert.AreEqual(new EventRecord(1, _prepare2, "test1", "eventType"), result.Records[0]);
			Assert.AreEqual(new EventRecord(0, _prepare1, "test1", "eventType"), result.Records[1]);
		}

		[Test]
		public void the_stream_can_be_read_as_a_whole_with_from_end() {
			var result = ReadIndex.ReadStreamEventsBackward("test1", -1, 2);
			Assert.AreEqual(ReadStreamResult.Success, result.Result);
			Assert.AreEqual(2, result.Records.Length);
			Assert.AreEqual(new EventRecord(1, _prepare2, "test1", "eventType"), result.Records[0]);
			Assert.AreEqual(new EventRecord(0, _prepare1, "test1", "eventType"), result.Records[1]);
		}

		[Test]
		public void the_stream_cant_be_read_for_second_stream() {
			var result = ReadIndex.ReadStreamEventsBackward("test2", 0, 1);
			Assert.AreEqual(ReadStreamResult.NoStream, result.Result);
			Assert.AreEqual(0, result.Records.Length);
		}

		[Test]
		public void read_all_events_forward_returns_all_events_in_correct_order() {
			var records = ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 10).EventRecords();

			Assert.AreEqual(2, records.Count);
			Assert.AreEqual(_id1, records[0].Event.EventId);
			Assert.AreEqual(_id2, records[1].Event.EventId);
		}

		[Test]
		public void read_all_events_backward_returns_all_events_in_correct_order() {
			var records = ReadIndex.ReadAllEventsBackward(GetBackwardReadPos(), 10).EventRecords();

			Assert.AreEqual(2, records.Count);
			Assert.AreEqual(_id1, records[1].Event.EventId);
			Assert.AreEqual(_id2, records[0].Event.EventId);
		}
	}
}
