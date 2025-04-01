// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Buffers;
using EventStore.Core.LogAbstraction;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.FileNamingStrategy;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Plugins.Transforms;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

public static class LogRecordExtensions {
	public static void WriteWithLengthPrefixAndSuffixTo(this ILogRecord record, BinaryWriter writer) {
		var localWriter = new BufferWriterSlim<byte>();
		try {
			record.WriteTo(ref localWriter);

			writer.Write(localWriter.WrittenCount);
			writer.Write(localWriter.WrittenSpan);
			writer.Write(localWriter.WrittenCount);
		} finally {
			localWriter.Dispose();
		}
	}
}

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_chasing_a_chunked_transaction_log<TLogFormat, TStreamId> : SpecificationWithDirectory {
	private readonly Guid _correlationId = Guid.NewGuid();
	private readonly Guid _eventId = Guid.NewGuid();

	[Test]
	public async Task try_read_returns_false_when_writer_checkpoint_is_zero() {
		var writerchk = new InMemoryCheckpoint(0);
		var chaserchk = new InMemoryCheckpoint();
		await using var db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, writerchk, chaserchk));
		await db.Open();

		var chaser = new TFChunkChaser(db, writerchk, new InMemoryCheckpoint());
		chaser.Open();

		Assert.IsTrue(await chaser.TryReadNext(CancellationToken.None) is { LogRecord: null });

		chaser.Close();
	}

	[Test]
	public async Task try_read_returns_false_when_writer_checksum_is_equal_to_reader_checksum() {
		var writerchk = new InMemoryCheckpoint();
		var chaserchk = new InMemoryCheckpoint(Checkpoint.Chaser, 0);
		await using var db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, writerchk, chaserchk));
		await db.Open();
		writerchk.Write(12);
		writerchk.Flush();
		chaserchk.Write(12);
		chaserchk.Flush();

		var chaser = new TFChunkChaser(db, writerchk, chaserchk);
		chaser.Open();

		Assert.IsTrue(await chaser.TryReadNext(CancellationToken.None) is { LogRecord: null });
		Assert.AreEqual(12, chaserchk.Read());

		chaser.Close();
	}

	[Test]
	public async Task try_read_returns_record_when_writerchecksum_ahead() {
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
			data: new byte[] {1, 2, 3, 4, 5},
			metadata: new byte[] {7, 17});

		using (var fs = new FileStream(GetFilePathFor("chunk-000000.000000"), FileMode.CreateNew,
			FileAccess.Write)) {
			fs.SetLength(ChunkHeader.Size + ChunkFooter.Size + 10000);
			var chunkHeader = new ChunkHeader(TFChunk.CurrentChunkVersion, TFChunk.CurrentChunkVersion, 10000, 0, 0, false, Guid.NewGuid(), TransformType.Identity)
				.AsByteArray();
			var writer = new BinaryWriter(fs);
			writer.Write(chunkHeader);
			recordToWrite.WriteWithLengthPrefixAndSuffixTo(writer);
			fs.Close();
		}

		var writerchk = new InMemoryCheckpoint(recordToWrite.GetSizeWithLengthPrefixAndSuffix() + 16);
		var chaserchk = new InMemoryCheckpoint(Checkpoint.Chaser, 0);
		await using var db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, writerchk, chaserchk));
		await db.Open();

		var chaser = new TFChunkChaser(db, writerchk, chaserchk);
		chaser.Open();

		var recordRead = await chaser.TryReadNext(CancellationToken.None);
		chaser.Close();

		Assert.AreEqual(recordRead.LogRecord.GetSizeWithLengthPrefixAndSuffix(), chaserchk.Read());
		Assert.IsTrue(recordRead.Success);
		Assert.AreEqual(recordToWrite, recordRead.LogRecord);
	}


	[Test]
	public async Task try_read_returns_record_when_record_bigger_than_internal_buffer() {
		var writerchk = new InMemoryCheckpoint(0);
		var chaserchk = new InMemoryCheckpoint(Checkpoint.Chaser, 0);

		await using var db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, writerchk, chaserchk));
		await db.Open();

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
			metadata: new byte[] {7, 17});
		var writer = new TFChunkWriter(db);
		await writer.Open(CancellationToken.None);

		Assert.IsTrue(await writer.Write(recordToWrite, CancellationToken.None) is (true, _));
		await writer.DisposeAsync();

		writerchk.Write(recordToWrite.GetSizeWithLengthPrefixAndSuffix());

		var reader = new TFChunkChaser(db, writerchk, chaserchk);
		reader.Open();

		var readRecord = await reader.TryReadNext(CancellationToken.None);
		reader.Close();

		Assert.IsTrue(readRecord.Success);
		Assert.AreEqual(readRecord.LogRecord.GetSizeWithLengthPrefixAndSuffix(), chaserchk.Read());
		Assert.AreEqual(recordToWrite, readRecord.LogRecord);
	}

	[Test]
	public async Task try_read_returns_record_when_writerchecksum_equal() {
		var writerchk = new InMemoryCheckpoint(0);
		var chaserchk = new InMemoryCheckpoint(Checkpoint.Chaser, 0);
		await using var db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, writerchk, chaserchk));
		await db.Open();

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
			data: new byte[] {1, 2, 3, 4, 5},
			metadata: new byte[] {7, 17});
		var writer = new TFChunkWriter(db);
		await writer.Open(CancellationToken.None);

		Assert.IsTrue(await writer.Write(recordToWrite, CancellationToken.None) is (true, _));
		await writer.DisposeAsync();

		writerchk.Write(recordToWrite.GetSizeWithLengthPrefixAndSuffix());

		var chaser = new TFChunkChaser(db, writerchk, chaserchk);
		chaser.Open();

		var readRecord = await chaser.TryReadNext(CancellationToken.None);
		chaser.Close();

		Assert.IsTrue(readRecord.Success);
		Assert.AreEqual(readRecord.LogRecord.GetSizeWithLengthPrefixAndSuffix(), chaserchk.Read());
		Assert.AreEqual(recordToWrite, readRecord.LogRecord);
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
