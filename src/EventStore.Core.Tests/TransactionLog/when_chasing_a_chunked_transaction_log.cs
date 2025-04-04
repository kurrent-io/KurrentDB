using System;
using System.IO;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog {
	public static class LogRecordExtensions {
		public static void WriteWithLengthPrefixAndSuffixTo(this ILogRecord record, BinaryWriter writer) {
			using (var memoryStream = new MemoryStream()) {
				record.WriteTo(new BinaryWriter(memoryStream));
				var length = (int)memoryStream.Length;
				writer.Write(length);
				writer.Write(memoryStream.GetBuffer(), 0, (int)memoryStream.Length);
				writer.Write(length);
			}
		}
	}

	[TestFixture(typeof(LogFormat.V2), typeof(string))]
	[TestFixture(typeof(LogFormat.V3), typeof(uint))]
	public class when_chasing_a_chunked_transaction_log<TLogFormat, TStreamId> : SpecificationWithDirectory {
		private readonly Guid _correlationId = Guid.NewGuid();
		private readonly Guid _eventId = Guid.NewGuid();

		[Test]
		public void try_read_returns_false_when_writer_checkpoint_is_zero() {
			var writerchk = new InMemoryCheckpoint(0);
			var chaserchk = new InMemoryCheckpoint();
			var db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, writerchk, chaserchk));
			db.Open();

			var chaser = new TFChunkChaser(db, writerchk, new InMemoryCheckpoint(), false);
			chaser.Open();

			ILogRecord record;
			Assert.IsFalse(chaser.TryReadNext(out record));

			chaser.Close();
			db.Dispose();
		}

		[Test]
		public void try_read_returns_false_when_writer_checksum_is_equal_to_reader_checksum() {
			var writerchk = new InMemoryCheckpoint();
			var chaserchk = new InMemoryCheckpoint(Checkpoint.Chaser, 0);
			var db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, writerchk, chaserchk));
			db.Open();
			writerchk.Write(12);
			writerchk.Flush();
			chaserchk.Write(12);
			chaserchk.Flush();

			var chaser = new TFChunkChaser(db, writerchk, chaserchk, false);
			chaser.Open();

			ILogRecord record;
			Assert.IsFalse(chaser.TryReadNext(out record));
			Assert.AreEqual(12, chaserchk.Read());

			chaser.Close();
			db.Dispose();
		}

		[Test]
		public void try_read_returns_record_when_writerchecksum_ahead() {
			var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
			var streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
			var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
			var recordToWrite = LogRecord.Prepare(
				factory: recordFactory,
				logPosition: 0,
				correlationId: _correlationId,
				eventId: _eventId,
				transactionPos: 0,
				transactionOffset: 0,
				eventStreamId: streamId,
				expectedVersion: 1234,
				timeStamp: new DateTime(2012, 12, 21),
				flags: PrepareFlags.None,
				eventType: eventTypeId,
				data: new byte[] { 1, 2, 3, 4, 5 },
				metadata: new byte[] { 7, 17 });

			using (var fs = new FileStream(GetFilePathFor("chunk-000000.000000"), FileMode.CreateNew,
				FileAccess.Write)) {
				fs.SetLength(ChunkHeader.Size + ChunkFooter.Size + 10000);
				var chunkHeader = new ChunkHeader(TFChunk.CurrentChunkVersion, 10000, 0, 0, false, Guid.NewGuid())
					.AsByteArray();
				var writer = new BinaryWriter(fs);
				writer.Write(chunkHeader);
				recordToWrite.WriteWithLengthPrefixAndSuffixTo(writer);
				fs.Close();
			}

			var writerchk = new InMemoryCheckpoint(recordToWrite.GetSizeWithLengthPrefixAndSuffix() + 16);
			var chaserchk = new InMemoryCheckpoint(Checkpoint.Chaser, 0);
			var db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, writerchk, chaserchk));
			db.Open();

			var chaser = new TFChunkChaser(db, writerchk, chaserchk, false);
			chaser.Open();

			ILogRecord record;
			var recordRead = chaser.TryReadNext(out record);
			chaser.Close();

			Assert.AreEqual(record.GetSizeWithLengthPrefixAndSuffix(), chaserchk.Read());
			Assert.IsTrue(recordRead);
			Assert.AreEqual(recordToWrite, record);

			db.Close();
		}


		[Test]
		public void try_read_returns_record_when_record_bigger_than_internal_buffer() {
			var writerchk = new InMemoryCheckpoint(0);
			var chaserchk = new InMemoryCheckpoint(Checkpoint.Chaser, 0);

			var db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, writerchk, chaserchk));
			db.Open();

			var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
			var streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
			var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;

			var recordToWrite = LogRecord.Prepare(
				factory: recordFactory,
				logPosition: 0,
				correlationId: _correlationId,
				eventId: _eventId,
				transactionPos: 0,
				transactionOffset: 0,
				eventStreamId: streamId,
				expectedVersion: 1234,
				timeStamp: new DateTime(2012, 12, 21),
				flags: PrepareFlags.None,
				eventType: eventTypeId,
				data: new byte[9000],
				metadata: new byte[] { 7, 17 });
			var writer = new TFChunkWriter(db);
			writer.Open();
			long pos;
			Assert.IsTrue(writer.Write(recordToWrite, out pos));
			writer.Close();

			writerchk.Write(recordToWrite.GetSizeWithLengthPrefixAndSuffix());

			var reader = new TFChunkChaser(db, writerchk, chaserchk, false);
			reader.Open();

			ILogRecord record;
			var readRecord = reader.TryReadNext(out record);
			reader.Close();

			Assert.IsTrue(readRecord);
			Assert.AreEqual(record.GetSizeWithLengthPrefixAndSuffix(), chaserchk.Read());
			Assert.AreEqual(recordToWrite, record);

			db.Close();
		}

		[Test]
		public void try_read_returns_record_when_writerchecksum_equal() {
			var writerchk = new InMemoryCheckpoint(0);
			var chaserchk = new InMemoryCheckpoint(Checkpoint.Chaser, 0);
			var db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, writerchk, chaserchk));
			db.Open();

			var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
			var streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
			var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;

			var recordToWrite = LogRecord.Prepare(
				factory: recordFactory,
				logPosition: 0,
				correlationId: _correlationId,
				eventId: _eventId,
				transactionPos: 0,
				transactionOffset: 0,
				eventStreamId: streamId,
				expectedVersion: 1234,
				timeStamp: new DateTime(2012, 12, 21),
				flags: PrepareFlags.None,
				eventType: eventTypeId,
				data: new byte[] { 1, 2, 3, 4, 5 },
				metadata: new byte[] { 7, 17 });
			var writer = new TFChunkWriter(db);
			writer.Open();
			long pos;
			Assert.IsTrue(writer.Write(recordToWrite, out pos));
			writer.Close();

			writerchk.Write(recordToWrite.GetSizeWithLengthPrefixAndSuffix());

			var chaser = new TFChunkChaser(db, writerchk, chaserchk, false);
			chaser.Open();

			ILogRecord record;
			var readRecord = chaser.TryReadNext(out record);
			chaser.Close();

			Assert.IsTrue(readRecord);
			Assert.AreEqual(record.GetSizeWithLengthPrefixAndSuffix(), chaserchk.Read());
			Assert.AreEqual(recordToWrite, record);

			db.Close();
		}

		/*   [Test]
		   public void try_read_returns_false_when_writer_checksum_is_ahead_but_not_enough_to_read_record()
		   {
		       var writerchk = new InMemoryCheckpoint(50);
		       var readerchk = new InMemoryCheckpoint("reader", 0);
		       var config = new TransactionFileDatabaseConfig(PathName, "prefix.tf", 10000, writerchk, new[] { readerchk });
		       var recordToWrite = LogRecord.Prepare(logPosition: 0,
		                                                correlationId: _correlationId,
		                                                eventId: _eventId,
		                                                transactionPosition: 0,
		                                                eventStreamId: "WorldEnding",
		                                                expectedVersion: 1234,
		                                                timeStamp: new DateTime(2012, 12, 21),
		                                                flags: PrepareFlags.None,
		                                                eventType: "type",
		                                                data: new byte[] { 1, 2, 3, 4, 5 },
		                                                metadata: new byte[] { 7, 17 });
		       using (var fs = new FileStream(GetFilePathFor("prefix.tf0"), FileMode.CreateNew, FileAccess.Write))
		       {
		           var writer = new BinaryWriter(fs);
		           recordToWrite.WriteWithLengthPrefixAndSuffixTo(writer);
		           fs.Close();
		       }
   
		       var reader = new MultifileTransactionFileChaser(config, "reader");
		       reader.Open();
		       LogRecord record = null;
		       var readRecord = reader.TryReadNext(out record);
		       reader.Close();
   
		       Assert.IsFalse(readRecord);
		       Assert.AreEqual(0, readerchk.Read());
		   }
   
		   [Test]
		   public void try_read_returns_false_when_writer_checksum_is_ahead_but_not_enough_to_read_length()
		   {
		       var writerchk = new InMemoryCheckpoint(3);
		       var readerchk = new InMemoryCheckpoint("reader", 0);
		       var config = new TransactionFileDatabaseConfig(PathName, "prefix.tf", 10000, writerchk,
		                                                      new List<ICheckpoint> { readerchk });
		       var recordToWrite = LogRecord.Prepare(logPosition: 0,
		                                                correlationId: _correlationId,
		                                                eventId: _eventId,
		                                                transactionPosition: 0,
		                                                eventStreamId: "WorldEnding",
		                                                expectedVersion: 1234,
		                                                timeStamp: new DateTime(2012, 12, 21),
		                                                flags: PrepareFlags.None,
		                                                eventType: "type",
		                                                data: new byte[] { 1, 2, 3, 4, 5 },
		                                                metadata: new byte[] { 7, 17 });
		       using (var fs = new FileStream(GetFilePathFor("prefix.tf0"), FileMode.CreateNew, FileAccess.Write))
		       {
		           var writer = new BinaryWriter(fs);
		           recordToWrite.WriteWithLengthPrefixAndSuffixTo(writer);
		           fs.Close();
		       }
   
		       var reader = new MultifileTransactionFileChaser(config, "reader");
		       reader.Open();
		       LogRecord record = null;
		       var readRecord = reader.TryReadNext(out record);
		       reader.Close();
		       Assert.IsFalse(readRecord);
		       Assert.AreEqual(0, readerchk.Read());
		   }
   
		   [Test]
		   public void try_read_returns_properly_when_writer_is_written_to_while_chasing()
		   {
		       var writerchk = new InMemoryCheckpoint(0);
		       var readerchk = new InMemoryCheckpoint("reader", 0);
		       var config = new TransactionFileDatabaseConfig(PathName, "prefix.tf", 10000, writerchk, new[] { readerchk });
   
		       var fileName = GetFilePathFor("prefix.tf0");
		       File.Create(fileName).Close();
   
		       var reader = new MultifileTransactionFileChaser(config, "reader");
		       reader.Open();
   
		       LogRecord record;
		       Assert.IsFalse(reader.TryReadNext(out record));
   
		       var recordToWrite = LogRecord.Prepare(logPosition: 0,
		                                                correlationId: _correlationId,
		                                                eventId: _eventId,
		                                                transactionPosition: 0,
		                                                eventStreamId: "WorldEnding",
		                                                expectedVersion: 1234,
		                                                timeStamp: new DateTime(2012, 12, 21),
		                                                flags: PrepareFlags.None,
		                                                eventType: "type",
		                                                data: new byte[] { 1, 2, 3, 4, 5 },
		                                                metadata: new byte[] { 7, 17 });
		       var memstream = new MemoryStream();
		       var writer = new BinaryWriter(memstream);
		       recordToWrite.WriteWithLengthPrefixAndSuffixTo(writer);
   
		       using (var fs = new FileStream(fileName, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
		       {
		           fs.Write(memstream.ToArray(), 0, (int)memstream.Length);
		           fs.Flush(flushToDisk: true);
		       }
		       writerchk.Write(memstream.Length);
   
		       Assert.IsTrue(reader.TryReadNext(out record));
		       Assert.AreEqual(record, recordToWrite);
   
		       var recordToWrite2 = LogRecord.Prepare(logPosition: 0,
		                                                 correlationId: _correlationId,
		                                                 eventId: _eventId,
		                                                 transactionPosition: 0,
		                                                 eventStreamId: "WorldEnding",
		                                                 expectedVersion: 4321,
		                                                 timeStamp: new DateTime(2012, 12, 21),
		                                                 flags: PrepareFlags.None,
		                                                 eventType: "type",
		                                                 data: new byte[] { 3, 2, 1 },
		                                                 metadata: new byte[] { 9 });
		       memstream.SetLength(0);
		       recordToWrite2.WriteWithLengthPrefixAndSuffixTo(writer);
   
		       using (var fs = new FileStream(fileName, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
		       {
		           fs.Write(memstream.ToArray(), 0, (int)memstream.Length);
		           fs.Flush(flushToDisk: true);
		       }
		       writerchk.Write(writerchk.Read() + memstream.Length);
   
		       Assert.IsTrue(reader.TryReadNext(out record));
		       Assert.AreEqual(record, recordToWrite2);
   
		       reader.Close();
		   }*/
	}
}
