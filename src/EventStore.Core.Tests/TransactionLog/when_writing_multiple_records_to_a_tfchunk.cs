using System;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_writing_multiple_records_to_a_tfchunk<TLogFormat, TStreamId> : SpecificationWithFilePerTestFixture {
		private TFChunk _chunk;
		private readonly Guid _corrId = Guid.NewGuid();
		private readonly Guid _eventId = Guid.NewGuid();
		private long _position1;
		private long _position2;
		private bool _written1;
		private bool _written2;

		private IPrepareLogRecord<TStreamId> _prepare1;
		private IPrepareLogRecord<TStreamId> _prepare2;

		[OneTimeSetUp]
		public override void TestFixtureSetUp() {
			base.TestFixtureSetUp();
			_chunk = TFChunkHelper.CreateNewChunk(Filename);

			var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
			var streamId1 = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
			var streamId2 = LogFormatHelper<TLogFormat, TStreamId>.StreamId2;
			var eventTypeId1 = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
			var eventTypeId2 = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId2;

			_prepare1 = LogRecord.Prepare(recordFactory, 0, _corrId, _eventId, 0, 0, streamId1, 1,
				PrepareFlags.None, eventTypeId1, new byte[12], new byte[15], new DateTime(2000, 1, 1, 12, 0, 0));
			var r1 = _chunk.TryAppend(_prepare1);
			_written1 = r1.Success;
			_position1 = r1.OldPosition;

			_prepare2 = LogRecord.Prepare(recordFactory, r1.NewPosition, _corrId, _eventId, 0, 0, streamId2, 2,
				PrepareFlags.None, eventTypeId2, new byte[12], new byte[15], new DateTime(2000, 1, 1, 12, 0, 0));
			var r2 = _chunk.TryAppend(_prepare2);
			_written2 = r2.Success;
			_position2 = r2.OldPosition;
			_chunk.Flush();
		}

		[OneTimeTearDown]
		public override void TestFixtureTearDown() {
			_chunk.Dispose();
			base.TestFixtureTearDown();
		}

		[Test]
		public void the_chunk_is_cached() {
			Assert.IsTrue(_chunk.IsCached);
		}

		[Test]
		public void the_first_record_was_written() {
			Assert.IsTrue(_written1);
		}

		[Test]
		public void the_second_record_was_written() {
			Assert.IsTrue(_written2);
		}

		[Test]
		public void the_first_record_can_be_read_at_position() {
			var res = _chunk.TryReadAt((int)_position1, couldBeScavenged: true);
			Assert.IsTrue(res.Success);
			Assert.IsTrue(res.LogRecord is IPrepareLogRecord<TStreamId>);
			Assert.AreEqual(_prepare1, res.LogRecord);
		}

		[Test]
		public void the_second_record_can_be_read_at_position() {
			var res = _chunk.TryReadAt((int)_position2, couldBeScavenged: true);
			Assert.IsTrue(res.Success);
			Assert.IsTrue(res.LogRecord is IPrepareLogRecord<TStreamId>);
			Assert.AreEqual(_prepare2, res.LogRecord);
		}

		[Test]
		public void the_first_record_can_be_read() {
			var res = _chunk.TryReadFirst();
			Assert.IsTrue(res.Success);
			Assert.AreEqual(_prepare1.GetSizeWithLengthPrefixAndSuffix(), res.NextPosition);
			Assert.IsTrue(res.LogRecord is IPrepareLogRecord<TStreamId>);
			Assert.AreEqual(_prepare1, res.LogRecord);
		}

		[Test]
		public void the_second_record_can_be_read_as_closest_forward_after_first() {
			var res = _chunk.TryReadClosestForward(_prepare1.GetSizeWithLengthPrefixAndSuffix());
			Assert.IsTrue(res.Success);
			Assert.AreEqual(_prepare1.GetSizeWithLengthPrefixAndSuffix()
							+ _prepare2.GetSizeWithLengthPrefixAndSuffix(), res.NextPosition);
			Assert.IsTrue(res.LogRecord is IPrepareLogRecord<TStreamId>);
			Assert.AreEqual(_prepare2, res.LogRecord);
		}

		[Test]
		public void cannot_read_past_second_record_with_closest_forward_method() {
			var res = _chunk.TryReadClosestForward(_prepare1.GetSizeWithLengthPrefixAndSuffix()
												   + _prepare2.GetSizeWithLengthPrefixAndSuffix());
			Assert.IsFalse(res.Success);
		}

		[Test]
		public void the_seconds_record_can_be_read_as_last() {
			var res = _chunk.TryReadLast();
			Assert.IsTrue(res.Success);
			Assert.AreEqual(_prepare1.GetSizeWithLengthPrefixAndSuffix(), res.NextPosition);
			Assert.AreEqual(_prepare2, res.LogRecord);
		}

		[Test]
		public void the_first_record_can_be_read_as_closest_backward_after_last() {
			var res = _chunk.TryReadClosestBackward(_prepare1.GetSizeWithLengthPrefixAndSuffix());
			Assert.IsTrue(res.Success);
			Assert.AreEqual(0, res.NextPosition);
			Assert.AreEqual(_prepare1, res.LogRecord);
		}

		[Test]
		public void cannot_read_backward_from_zero_pos() {
			var res = _chunk.TryReadClosestBackward(0);
			Assert.IsFalse(res.Success);
		}
	}
}
