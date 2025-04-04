using EventStore.Core.Data;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.Metastreams {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class
		when_having_deleted_stream_and_no_metastream_metastream_is_declared_deleted_as_well<TLogFormat, TStreamId>
		: SimpleDbTestScenario<TLogFormat, TStreamId> {
		protected override DbResult CreateDb(TFChunkDbCreationHelper<TLogFormat, TStreamId> dbCreator) {
			return dbCreator.Chunk(Rec.Prepare(0, "test"),
					Rec.Commit(0, "test"),
					Rec.Delete(2, "test"),
					Rec.Commit(2, "test"))
				.CreateDb();
		}

		[Test]
		public void the_stream_is_deleted() {
			Assert.IsTrue(ReadIndex.IsStreamDeleted("test"));
		}

		[Test]
		public void the_metastream_is_deleted() {
			Assert.IsTrue(ReadIndex.IsStreamDeleted("$$test"));
		}

		[Test]
		public void get_last_event_number_reports_deleted_metastream() {
			Assert.AreEqual(EventNumber.DeletedStream, ReadIndex.GetStreamLastEventNumber("$$test"));
		}

		[Test]
		public void single_event_read_reports_deleted_metastream() {
			Assert.AreEqual(ReadEventResult.StreamDeleted, ReadIndex.ReadEvent("$$test", 0).Result);
		}

		[Test]
		public void last_event_read_reports_deleted_metastream() {
			Assert.AreEqual(ReadEventResult.StreamDeleted, ReadIndex.ReadEvent("$$test", -1).Result);
		}

		[Test]
		public void read_stream_events_forward_reports_deleted_metastream() {
			Assert.AreEqual(ReadStreamResult.StreamDeleted, ReadIndex.ReadStreamEventsForward("$$test", 0, 100).Result);
		}

		[Test]
		public void read_stream_events_backward_reports_deleted_metastream() {
			Assert.AreEqual(ReadStreamResult.StreamDeleted,
				ReadIndex.ReadStreamEventsBackward("$$test", 0, 100).Result);
		}
	}
}
