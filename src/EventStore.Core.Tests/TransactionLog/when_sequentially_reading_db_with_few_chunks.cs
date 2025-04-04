using System;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog {
	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_sequentially_reading_db_with_few_chunks<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
		private const int RecordsCount = 8;

		private TFChunkDb _db;
		private ILogRecord[] _records;
		private RecordWriteResult[] _results;

		public override async Task TestFixtureSetUp() {
			await base.TestFixtureSetUp();

			_db = new TFChunkDb(TFChunkHelper.CreateSizedDbConfig(PathName, 0, chunkSize: 4096));
			_db.Open();

			var chunk = _db.Manager.GetChunk(0);

			_records = new ILogRecord[RecordsCount];
			_results = new RecordWriteResult[RecordsCount];

			var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
			var streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
			var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
			var expectedVersion = ExpectedVersion.NoStream;

			var pos = 0;
			for (int i = 0; i < RecordsCount; ++i) {
				if (i > 0 && i % 3 == 0) {
					pos = i / 3 * _db.Config.ChunkSize;
					chunk.Complete();
					chunk = _db.Manager.AddNewChunk();
				}

				_records[i] = LogRecord.SingleWrite(recordFactory, pos,
					Guid.NewGuid(), Guid.NewGuid(), streamId, expectedVersion++, eventTypeId,
					new byte[1200], new byte[] { 5, 7 });
				_results[i] = chunk.TryAppend(_records[i]);

				pos += _records[i].GetSizeWithLengthPrefixAndSuffix();
			}

			chunk.Flush();
			_db.Config.WriterCheckpoint.Write((RecordsCount / 3) * _db.Config.ChunkSize +
											  _results[RecordsCount - 1].NewPosition);
			_db.Config.WriterCheckpoint.Flush();
		}

		public override Task TestFixtureTearDown() {
			_db.Dispose();

			return base.TestFixtureTearDown();
		}

		[Test]
		public void all_records_were_written() {
			var pos = 0;
			for (int i = 0; i < RecordsCount; ++i) {
				if (i % 3 == 0)
					pos = 0;

				Assert.IsTrue(_results[i].Success);
				Assert.AreEqual(pos, _results[i].OldPosition);

				pos += _records[i].GetSizeWithLengthPrefixAndSuffix();
				Assert.AreEqual(pos, _results[i].NewPosition);
			}
		}

		[Test]
		public void all_records_could_be_read_with_forward_pass() {
			var seqReader = new TFChunkReader(_db, _db.Config.WriterCheckpoint, 0);

			SeqReadResult res;
			int count = 0;
			while ((res = seqReader.TryReadNext()).Success) {
				var rec = _records[count];
				Assert.AreEqual(rec, res.LogRecord);
				Assert.AreEqual(rec.LogPosition, res.RecordPrePosition);
				Assert.AreEqual(rec.LogPosition + rec.GetSizeWithLengthPrefixAndSuffix(), res.RecordPostPosition);

				++count;
			}

			Assert.AreEqual(RecordsCount, count);
		}

		[Test]
		public void all_records_could_be_read_with_backward_pass() {
			var seqReader = new TFChunkReader(_db, _db.Config.WriterCheckpoint, _db.Config.WriterCheckpoint.Read());

			SeqReadResult res;
			int count = 0;
			while ((res = seqReader.TryReadPrev()).Success) {
				var rec = _records[RecordsCount - count - 1];
				Assert.AreEqual(rec, res.LogRecord);
				Assert.AreEqual(rec.LogPosition, res.RecordPrePosition);
				Assert.AreEqual(rec.LogPosition + rec.GetSizeWithLengthPrefixAndSuffix(), res.RecordPostPosition);

				++count;
			}

			Assert.AreEqual(RecordsCount, count);
		}

		[Test]
		public void all_records_could_be_read_doing_forward_backward_pass() {
			var seqReader = new TFChunkReader(_db, _db.Config.WriterCheckpoint, 0);

			SeqReadResult res;
			int count1 = 0;
			while ((res = seqReader.TryReadNext()).Success) {
				var rec = _records[count1];
				Assert.AreEqual(rec, res.LogRecord);
				Assert.AreEqual(rec.LogPosition, res.RecordPrePosition);
				Assert.AreEqual(rec.LogPosition + rec.GetSizeWithLengthPrefixAndSuffix(), res.RecordPostPosition);

				++count1;
			}

			Assert.AreEqual(RecordsCount, count1);

			int count2 = 0;
			while ((res = seqReader.TryReadPrev()).Success) {
				var rec = _records[RecordsCount - count2 - 1];
				Assert.AreEqual(rec, res.LogRecord);
				Assert.AreEqual(rec.LogPosition, res.RecordPrePosition);
				Assert.AreEqual(rec.LogPosition + rec.GetSizeWithLengthPrefixAndSuffix(), res.RecordPostPosition);

				++count2;
			}

			Assert.AreEqual(RecordsCount, count2);
		}

		[Test]
		public void records_can_be_read_forward_starting_from_any_position() {
			for (int i = 0; i < RecordsCount; ++i) {
				var seqReader = new TFChunkReader(_db, _db.Config.WriterCheckpoint, _records[i].LogPosition);

				SeqReadResult res;
				int count = 0;
				while ((res = seqReader.TryReadNext()).Success) {
					var rec = _records[i + count];
					Assert.AreEqual(rec, res.LogRecord);
					Assert.AreEqual(rec.LogPosition, res.RecordPrePosition);
					Assert.AreEqual(rec.LogPosition + rec.GetSizeWithLengthPrefixAndSuffix(), res.RecordPostPosition);

					++count;
				}

				Assert.AreEqual(RecordsCount - i, count);
			}
		}

		[Test]
		public void records_can_be_read_backward_starting_from_any_position() {
			for (int i = 0; i < RecordsCount; ++i) {
				var seqReader = new TFChunkReader(_db, _db.Config.WriterCheckpoint, _records[i].LogPosition);

				SeqReadResult res;
				int count = 0;
				while ((res = seqReader.TryReadPrev()).Success) {
					var rec = _records[i - count - 1];
					Assert.AreEqual(rec, res.LogRecord);
					Assert.AreEqual(rec.LogPosition, res.RecordPrePosition);
					Assert.AreEqual(rec.LogPosition + rec.GetSizeWithLengthPrefixAndSuffix(), res.RecordPostPosition);

					++count;
				}

				Assert.AreEqual(i, count);
			}
		}
	}
}
