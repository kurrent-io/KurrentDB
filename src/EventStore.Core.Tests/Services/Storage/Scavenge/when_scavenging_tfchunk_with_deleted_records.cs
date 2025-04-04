using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.LogCommon;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.Scavenge {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_scavenging_tfchunk_with_deleted_records<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
		private const string _eventStreamId = "ES";
		private const string _deletedEventStreamId = "Deleted-ES";
		private EventRecord _event1, _event2, _event3, _event4, _deleted;

		protected override void WriteTestScenario() {
			// Stream that will be kept
			_event1 = WriteSingleEvent(_eventStreamId, 0, "bla1");
			_event2 = WriteSingleEvent(_eventStreamId, 1, "bla1");

			// Stream that will be deleted
			WriteSingleEvent(_deletedEventStreamId, 0, "bla1");
			WriteSingleEvent(_deletedEventStreamId, 1, "bla1");
			_deleted = WriteDelete(_deletedEventStreamId);

			// Stream that will be kept
			_event3 = WriteSingleEvent(_eventStreamId, 2, "bla1");
			_event4 = WriteSingleEvent(_eventStreamId, 3, "bla1");

			Writer.CompleteChunk();

			Scavenge(completeLast: false, mergeChunks: true);
		}

		[Test]
		public void should_be_able_to_read_the_all_stream() {
			var events = ReadIndex.ReadAllEventsForward(new TFPos(0, 0), 100).EventRecords()
				.Select(r => r.Event)
				.ToArray();
			Assert.AreEqual(5, events.Count());
			Assert.AreEqual(_event1.EventId, events[0].EventId);
			Assert.AreEqual(_event2.EventId, events[1].EventId);
			Assert.AreEqual(_deleted.EventId, events[2].EventId);
			Assert.AreEqual(_event3.EventId, events[3].EventId);
			Assert.AreEqual(_event4.EventId, events[4].EventId);
		}

		[Test]
		public void should_have_updated_deleted_stream_event_number() {
			var chunk = Db.Manager.GetChunk(0);
			var chunkRecords = new List<ILogRecord>();
			RecordReadResult result = chunk.TryReadFirst();
			while (result.Success) {
				chunkRecords.Add(result.LogRecord);
				result = chunk.TryReadClosestForward(result.NextPosition);
			}

			var id = _logFormat.StreamIds.LookupValue(_deletedEventStreamId);
			var deletedRecord = (IPrepareLogRecord<TStreamId>)chunkRecords.First(
				x => x.RecordType == LogRecordType.Prepare
					 && EqualityComparer<TStreamId>.Default.Equals(((IPrepareLogRecord<TStreamId>)x).EventStreamId, id));

			Assert.AreEqual(EventNumber.DeletedStream - 1, deletedRecord.ExpectedVersion);
		}

		[Test]
		public void should_be_able_to_read_the_stream() {
			var events = ReadIndex.ReadStreamEventsForward(_eventStreamId, 0, 10);
			Assert.AreEqual(4, events.Records.Length);
			Assert.AreEqual(_event1.EventId, events.Records[0].EventId);
			Assert.AreEqual(_event2.EventId, events.Records[1].EventId);
			Assert.AreEqual(_event3.EventId, events.Records[2].EventId);
			Assert.AreEqual(_event4.EventId, events.Records[3].EventId);
		}

		[Test]
		public void the_deleted_stream_should_be_deleted() {
			var lastNumber = ReadIndex.GetStreamLastEventNumber(_deletedEventStreamId);
			Assert.AreEqual(EventNumber.DeletedStream, lastNumber);
		}
	}
}
