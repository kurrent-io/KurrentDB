using System;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.LogCommon;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog {
	[TestFixture]
	public class when_writing_commit_record_to_file : SpecificationWithDirectoryPerTestFixture {
		private ITransactionFileWriter _writer;
		private InMemoryCheckpoint _writerCheckpoint;
		private readonly Guid _eventId = Guid.NewGuid();
		private CommitLogRecord _record;
		private TFChunkDb _db;

		[OneTimeSetUp]
		public void SetUp() {
			_writerCheckpoint = new InMemoryCheckpoint();
			_db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, _writerCheckpoint, new InMemoryCheckpoint(),
				1024));
			_db.Open();
			_writer = new TFChunkWriter(_db);
			_writer.Open();
			_record = new CommitLogRecord(logPosition: 0,
				correlationId: _eventId,
				transactionPosition: 4321,
				timeStamp: new DateTime(2012, 12, 21),
				firstEventNumber: 10);
			long newPos;
			_writer.Write(_record, out newPos);
			_writer.Flush();
		}

		[OneTimeTearDown]
		public void Teardown() {
			_writer.Close();
			_db.Close();
		}

		[Test]
		public void the_data_is_written() {
			using (var reader = new TFChunkChaser(_db, _writerCheckpoint, _db.Config.ChaserCheckpoint, false)) {
				reader.Open();
				ILogRecord r;
				Assert.IsTrue(reader.TryReadNext(out r));

				Assert.True(r is CommitLogRecord);
				var c = (CommitLogRecord)r;
				Assert.AreEqual(c.RecordType, LogRecordType.Commit);
				Assert.AreEqual(c.LogPosition, 0);
				Assert.AreEqual(c.CorrelationId, _eventId);
				Assert.AreEqual(c.TransactionPosition, 4321);
				Assert.AreEqual(c.TimeStamp, new DateTime(2012, 12, 21));
			}
		}

		[Test]
		public void the_checksum_is_updated() {
			Assert.AreEqual(_record.GetSizeWithLengthPrefixAndSuffix(), _writerCheckpoint.Read());
		}

		[Test]
		public void trying_to_read_past_writer_checksum_returns_false() {
			var reader = new TFChunkReader(_db, _writerCheckpoint);
			Assert.IsFalse(reader.TryReadAt(_writerCheckpoint.Read(), couldBeScavenged: true).Success);
		}
	}
}
