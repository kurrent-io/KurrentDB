// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNext.IO;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_writing_a_new_chunked_transaction_file<TLogFormat, TStreamId> : SpecificationWithDirectory {
	private readonly Guid _eventId = Guid.NewGuid();
	private readonly Guid _correlationId = Guid.NewGuid();
	private InMemoryCheckpoint _checkpoint;

	[Test]
	public async Task a_record_can_be_written() {
		_checkpoint = new InMemoryCheckpoint(0);
		var db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, _checkpoint, new InMemoryCheckpoint()));
		await db.Open();
		var tf = new TFChunkWriter(db);
		await tf.Open(CancellationToken.None);

		var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
		var streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;

		var record = LogRecord.Prepare(
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

		await tf.Write(record, CancellationToken.None);
		await tf.DisposeAsync();
		await db.DisposeAsync();

		Assert.AreEqual(record.GetSizeWithLengthPrefixAndSuffix(), _checkpoint.Read());
		await using var filestream = File.Open(GetFilePathFor("chunk-000000.000000"), FileMode.Open, FileAccess.Read);
		filestream.Position = ChunkHeader.Size;

		var recordLength = await filestream.ReadLittleEndianAsync<int>(new byte[sizeof(int)]);
		var buffer = new byte[recordLength];
		await filestream.ReadExactlyAsync(buffer);

		var reader = new SequenceReader(new(buffer));
		var read = LogRecord.ReadFrom(ref reader);
		Assert.AreEqual(record, read);
	}
}
