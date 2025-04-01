// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.LogV2;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.FileNamingStrategy;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_reading_a_single_record<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private const int RecordsCount = 8;

	private TFChunkDb _db;
	private ILogRecord[] _records;
	private RecordWriteResult[] _results;

	public override async Task TestFixtureSetUp() {
		await base.TestFixtureSetUp();
		_db = new TFChunkDb(TFChunkHelper.CreateSizedDbConfig(PathName, 0, chunkSize: 4096));
		await _db.Open();

		var chunk = await _db.Manager.GetInitializedChunk(0, CancellationToken.None);
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
				await chunk.Complete(CancellationToken.None);
				chunk = await _db.Manager.AddNewChunk(CancellationToken.None);
			}

			_records[i] = LogRecord.SingleWrite(recordFactory, pos,
				Guid.NewGuid(), Guid.NewGuid(), streamId, expectedVersion++, eventTypeId,
				new byte[1200], new byte[] { 5, 7 });
			_results[i] = await chunk.TryAppend(_records[i], CancellationToken.None);

			pos += _records[i].GetSizeWithLengthPrefixAndSuffix();
		}

		await chunk.Flush(CancellationToken.None);
		_db.Config.WriterCheckpoint.Write((RecordsCount / 3) * _db.Config.ChunkSize +
										  _results[RecordsCount - 1].NewPosition);
		_db.Config.WriterCheckpoint.Flush();
	}

	public override async Task TestFixtureTearDown() {
		await _db.DisposeAsync();

		await base.TestFixtureTearDown();
	}

	private TFChunkReader GetTFChunkReader(long from) {
		return new TFChunkReader(_db, _db.Config.WriterCheckpoint, from);
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
	public async Task all_records_can_be_read() {
		var reader = GetTFChunkReader(0);

		RecordReadResult res;
		for (var i = 0; i < RecordsCount; i++) {
			var rec = _records[i];
			res = await reader.TryReadAt(rec.LogPosition, couldBeScavenged: true, CancellationToken.None);

			Assert.IsTrue(res.Success);
			Assert.AreEqual(rec, res.LogRecord);
		}
	}
}
