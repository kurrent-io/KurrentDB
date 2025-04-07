// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_sequentially_reading_db_with_one_chunk_ending_with_prepare<TLogFormat, TStreamId> :
	SpecificationWithDirectoryPerTestFixture {
	private const int RecordsCount = 3;

	private TFChunkDb _db;
	private ILogRecord[] _records;
	private RecordWriteResult[] _results;

	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();

		_db = new TFChunkDb(
			TFChunkHelper.CreateSizedDbConfig(PathName, 0, chunkSize: 4096));
		_db.Open();

		var chunk = _db.Manager.GetChunk(0);

		_records = new ILogRecord[RecordsCount];
		_results = new RecordWriteResult[RecordsCount];
		var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
		var streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		var expectedVersion = ExpectedVersion.NoStream;

		for (int i = 0; i < _records.Length - 1; ++i) {
			_records[i] = LogRecord.SingleWrite(
				recordFactory,
				i == 0 ? 0 : _results[i - 1].NewPosition,
				Guid.NewGuid(),
				Guid.NewGuid(),
				streamId,
				expectedVersion++,
				eventTypeId,
				new byte[] { 0, 1, 2 },
				new byte[] { 5, 7 });
			_results[i] = chunk.TryAppend(_records[i]);
		}

		_records[_records.Length - 1] = LogRecord.Prepare(
			recordFactory,
			_results[_records.Length - 1 - 1].NewPosition,
			Guid.NewGuid(),
			Guid.NewGuid(),
			_results[_records.Length - 1 - 1].NewPosition,
			0,
			streamId,
			expectedVersion++,
			PrepareFlags.Data,
			eventTypeId,
			new byte[] { 0, 1, 2 },
			new byte[] { 5, 7 });
		_results[_records.Length - 1] = chunk.TryAppend(_records[_records.Length - 1]);

		chunk.Flush();
		_db.Config.WriterCheckpoint.Write(_results[RecordsCount - 1].NewPosition);
		_db.Config.WriterCheckpoint.Flush();
	}

	public override Task TestFixtureTearDown() {
		_db.Dispose();

		return base.TestFixtureTearDown();
	}

	[Test]
	public void only_the_last_record_is_marked_eof() {
		var seqReader = new TFChunkReader(_db, _db.Config.WriterCheckpoint, 0);

		SeqReadResult res;
		int count = 0;
		while ((res = seqReader.TryReadNext()).Success) {
			++count;
			Assert.AreEqual(count == RecordsCount, res.Eof);
		}

		Assert.AreEqual(RecordsCount, count);
	}
}
