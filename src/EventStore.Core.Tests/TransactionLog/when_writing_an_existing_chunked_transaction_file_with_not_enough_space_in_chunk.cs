// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNext.IO;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Plugins.Transforms;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_writing_an_existing_chunked_transaction_file_with_not_enough_space_in_chunk<TLogFormat, TStreamId> : SpecificationWithDirectory {
	private readonly Guid _correlationId = Guid.NewGuid();
	private readonly Guid _eventId = Guid.NewGuid();
	private InMemoryCheckpoint _checkpoint;

	[Test]
	public async Task a_record_is_not_written_at_first_but_written_on_second_try() {
		var filename1 = GetFilePathFor("chunk-000000.000000");
		var filename2 = GetFilePathFor("chunk-000001.000000");
		var chunkHeader = new ChunkHeader(TFChunk.CurrentChunkVersion, TFChunk.CurrentChunkVersion, 10000, 0, 0, false,
			Guid.NewGuid(), TransformType.Identity);
		var chunkBytes = chunkHeader.AsByteArray();
		var bytes = new byte[ChunkHeader.Size + 10000 + ChunkFooter.Size];
		Buffer.BlockCopy(chunkBytes, 0, bytes, 0, chunkBytes.Length);
		File.WriteAllBytes(filename1, bytes);

		_checkpoint = new InMemoryCheckpoint(0);
		var db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, _checkpoint, new InMemoryCheckpoint()));
		await db.Open();
		var tf = new TFChunkWriter(db);
		await tf.Open(CancellationToken.None);

		var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
		var streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;

		var record1 = LogRecord.Prepare(
			factory: recordFactory,
			logPosition: 0,
			correlationId: _correlationId,
			eventId: _eventId,
			expectedVersion: 1234,
			transactionPos: 0,
			transactionOffset: 0,
			eventStreamId: streamId,
			timeStamp: new DateTime(2012, 12, 21),
			flags: PrepareFlags.None,
			eventType: eventTypeId,
			data: new byte[] { 1, 2, 3, 4, 5 },
			metadata: new byte[8000]);

		var (written, pos) = await tf.Write(record1, CancellationToken.None);
		Assert.IsTrue(written); // almost fill up first chunk

		var record2 = LogRecord.Prepare(
			factory: recordFactory,
			logPosition: pos,
			correlationId: _correlationId,
			eventId: _eventId,
			expectedVersion: 1234,
			transactionPos: pos,
			transactionOffset: 0,
			eventStreamId: streamId,
			timeStamp: new DateTime(2012, 12, 21),
			flags: PrepareFlags.None,
			eventType: eventTypeId,
			data: new byte[] { 1, 2, 3, 4, 5 },
			metadata: new byte[8000]);

		(written, pos) = await tf.Write(record2, CancellationToken.None);
		Assert.IsFalse(written); // chunk has too small space

		var record3 = LogRecord.Prepare(
			factory: recordFactory,
			logPosition: pos,
			correlationId: _correlationId,
			eventId: _eventId,
			expectedVersion: 1234,
			transactionPos: pos,
			transactionOffset: 0,
			eventStreamId: streamId,
			timeStamp: new DateTime(2012, 12, 21),
			flags: PrepareFlags.None,
			eventType: eventTypeId,
			data: new byte[] { 1, 2, 3, 4, 5 },
			metadata: new byte[2000]);

		(written, _) = await tf.Write(record3, CancellationToken.None);
		Assert.IsTrue(written);
		await tf.DisposeAsync();
		await db.DisposeAsync();

		Assert.AreEqual(record3.GetSizeWithLengthPrefixAndSuffix() + 10000, _checkpoint.Read());
		await using var filestream = File.Open(filename2, new FileStreamOptions
			{ Mode = FileMode.Open, Access = FileAccess.Read, Options = FileOptions.Asynchronous });
		filestream.Seek(ChunkHeader.Size + sizeof(int), SeekOrigin.Begin);

		var recordLength = filestream.Length - filestream.Position;
		var buffer = new byte[recordLength];
		await filestream.ReadExactlyAsync(buffer);

		var reader = new SequenceReader(new(buffer));
		var read = LogRecord.ReadFrom(ref reader);
		Assert.AreEqual(record3, read);
	}
}
