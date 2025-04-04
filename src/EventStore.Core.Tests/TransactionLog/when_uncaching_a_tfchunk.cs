// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.Transforms;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_uncaching_a_tfchunk<TLogFormat, TStreamId> : SpecificationWithFilePerTestFixture {
	private TFChunk _chunk;
	private readonly Guid _corrId = Guid.NewGuid();
	private readonly Guid _eventId = Guid.NewGuid();
	private RecordWriteResult _result;
	private IPrepareLogRecord<TStreamId> _record;
	private TFChunk _uncachedChunk;

	[OneTimeSetUp]
	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
		var streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;

		_record = LogRecord.Prepare(recordFactory, 0, _corrId, _eventId, 0, 0, streamId, 1,
			PrepareFlags.None, eventTypeId, new byte[12], new byte[15], new DateTime(2000, 1, 1, 12, 0, 0));
		_chunk = await TFChunkHelper.CreateNewChunk(Filename);
		_result = await _chunk.TryAppend(_record, CancellationToken.None);
		await _chunk.Flush(CancellationToken.None);
		await _chunk.Complete(CancellationToken.None);
		_uncachedChunk = await TFChunk.FromCompletedFile(new ChunkLocalFileSystem(Path.GetDirectoryName(Filename)), Filename, verifyHash: true, unbufferedRead: false,
			reduceFileCachePressure: false, tracker: new TFChunkTracker.NoOp(),
			getTransformFactory: DbTransformManager.Default);
		await _uncachedChunk.CacheInMemory(CancellationToken.None);
		await _uncachedChunk.UnCacheFromMemory(CancellationToken.None);
	}

	[OneTimeTearDown]
	public override void TestFixtureTearDown() {
		_chunk.Dispose();
		_uncachedChunk.Dispose();
		base.TestFixtureTearDown();
	}

	[Test]
	public void the_write_result_is_correct() {
		Assert.IsTrue(_result.Success);
		Assert.AreEqual(0, _result.OldPosition);
		Assert.AreEqual(_record.GetSizeWithLengthPrefixAndSuffix(), _result.NewPosition);
	}

	[Test]
	public void the_chunk_is_not_cached() {
		Assert.IsFalse(_uncachedChunk.IsCached);
	}

	[Test]
	public void the_record_was_written() {
		Assert.IsTrue(_result.Success);
	}

	[Test]
	public void the_correct_position_is_returned() {
		Assert.AreEqual(0, _result.OldPosition);
	}

	[Test]
	public async Task the_record_can_be_read() {
		var res = await _uncachedChunk.TryReadAt(0, couldBeScavenged: true, CancellationToken.None);
		Assert.IsTrue(res.Success);
		Assert.AreEqual(_record, res.LogRecord);
		Assert.AreEqual(_result.OldPosition, res.LogRecord.LogPosition);
		//Assert.AreEqual(_result.NewPosition, res.NewPosition);
	}
}
