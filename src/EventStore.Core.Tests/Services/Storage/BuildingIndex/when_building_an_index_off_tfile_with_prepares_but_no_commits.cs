using System;
using EventStore.Core.Data;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.BuildingIndex {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_building_an_index_off_tfile_with_prepares_but_no_commits<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
		protected override void WriteTestScenario() {
			GetOrReserve("test1", out var streamId1, out _);
			GetOrReserve("test2", out var streamId2, out _);
			GetOrReserve("test3", out var streamId3, out var p0);

			var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;

			long p1;
			Writer.Write(LogRecord.Prepare(_logFormat.RecordFactory, p0, Guid.NewGuid(), Guid.NewGuid(), p0, 0, streamId1, -1,
					PrepareFlags.SingleWrite, eventTypeId, new byte[0], new byte[0], DateTime.UtcNow),
				out p1);
			long p2;
			Writer.Write(LogRecord.Prepare(_logFormat.RecordFactory, p1, Guid.NewGuid(), Guid.NewGuid(), p1, 0, streamId2, -1,
					PrepareFlags.SingleWrite, eventTypeId, new byte[0], new byte[0], DateTime.UtcNow),
				out p2);
			long p3;
			Writer.Write(LogRecord.Prepare(_logFormat.RecordFactory, p2, Guid.NewGuid(), Guid.NewGuid(), p2, 0, streamId3, -1,
					PrepareFlags.SingleWrite, eventTypeId, new byte[0], new byte[0], DateTime.UtcNow),
				out p3);
		}

		[Test]
		public void the_first_stream_is_not_in_index_yet() {
			var result = ReadIndex.ReadEvent("test1", 0);
			Assert.AreEqual(ReadEventResult.NoStream, result.Result);
			Assert.IsNull(result.Record);
		}

		[Test]
		public void the_second_stream_is_not_in_index_yet() {
			var result = ReadIndex.ReadEvent("test2", 0);
			Assert.AreEqual(ReadEventResult.NoStream, result.Result);
			Assert.IsNull(result.Record);
		}

		[Test]
		public void the_last_event_is_not_returned_for_stream() {
			var result = ReadIndex.ReadEvent("test2", -1);
			Assert.AreEqual(ReadEventResult.NoStream, result.Result);
			Assert.IsNull(result.Record);
		}

		[Test]
		public void read_all_events_forward_returns_no_events() {
			var records = ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 10).EventRecords();
			Assert.AreEqual(0, records.Count);
		}

		[Test]
		public void read_all_events_backward_returns_no_events() {
			var records = ReadIndex.ReadAllEventsBackward(GetBackwardReadPos(), 10).EventRecords();
			Assert.AreEqual(0, records.Count);
		}
	}
}
