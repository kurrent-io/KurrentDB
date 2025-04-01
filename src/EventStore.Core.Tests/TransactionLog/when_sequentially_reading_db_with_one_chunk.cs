// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.LogV2;
using EventStore.Core.Tests.TransactionLog;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Checkpoint;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.FileNamingStrategy;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_sequentially_reading_db_with_one_chunk<TLogFormat, TStreamId> : SpecificationWithDirectoryPerTestFixture {
	private const int RecordsCount = 3;

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

		for (int i = 0; i < _records.Length; ++i) {
			_records[i] = LogRecord.SingleWrite(recordFactory, i == 0 ? 0 : _results[i - 1].NewPosition,
				Guid.NewGuid(), Guid.NewGuid(), streamId, expectedVersion++, eventTypeId,
				new byte[] { 0, 1, 2 }, new byte[] { 5, 7 });
			_results[i] = await chunk.TryAppend(_records[i], CancellationToken.None);
		}

		await chunk.Flush(CancellationToken.None);
		_db.Config.WriterCheckpoint.Write(_results[RecordsCount - 1].NewPosition);
		_db.Config.WriterCheckpoint.Flush();
	}

	public override async Task TestFixtureTearDown() {
		await _db.DisposeAsync();

		await base.TestFixtureTearDown();
	}

	[Test]
	public void all_records_were_written() {
		var pos = 0;
		for (int i = 0; i < RecordsCount; ++i) {
			Assert.IsTrue(_results[i].Success);
			Assert.AreEqual(pos, _results[i].OldPosition);

			pos += _records[i].GetSizeWithLengthPrefixAndSuffix();
			Assert.AreEqual(pos, _results[i].NewPosition);
		}
	}

	[Test]
	public async Task all_records_could_be_read_with_forward_pass() {
		var seqReader = new TFChunkReader(_db, _db.Config.WriterCheckpoint, 0);

		int count = 0;
		while (await seqReader.TryReadNext(CancellationToken.None) is { Success: true } res) {
			var rec = _records[count];
			Assert.AreEqual(rec, res.LogRecord);
			Assert.AreEqual(rec.LogPosition, res.RecordPrePosition);
			Assert.AreEqual(rec.LogPosition + rec.GetSizeWithLengthPrefixAndSuffix(), res.RecordPostPosition);

			++count;
		}

		Assert.AreEqual(RecordsCount, count);
	}

	[Test]
	public async Task only_the_last_record_is_marked_eof() {
		var seqReader = new TFChunkReader(_db, _db.Config.WriterCheckpoint, 0);

		int count = 0;
		while (await seqReader.TryReadNext(CancellationToken.None) is { Success: true } res) {
			++count;
			Assert.AreEqual(count == RecordsCount, res.Eof);
		}

		Assert.AreEqual(RecordsCount, count);
	}

	[Test]
	public async Task all_records_could_be_read_with_backward_pass() {
		var seqReader = new TFChunkReader(_db, _db.Config.WriterCheckpoint, _db.Config.WriterCheckpoint.Read());

		SeqReadResult res;
		int count = 0;
		while ((res = await seqReader.TryReadPrev(CancellationToken.None)).Success) {
			var rec = _records[RecordsCount - count - 1];
			Assert.AreEqual(rec, res.LogRecord);
			Assert.AreEqual(rec.LogPosition, res.RecordPrePosition);
			Assert.AreEqual(rec.LogPosition + rec.GetSizeWithLengthPrefixAndSuffix(), res.RecordPostPosition);

			++count;
		}

		Assert.AreEqual(RecordsCount, count);
	}

	[Test]
	public async Task all_records_could_be_read_doing_forward_backward_pass() {
		var seqReader = new TFChunkReader(_db, _db.Config.WriterCheckpoint, 0);

		int count1 = 0;
		while (await seqReader.TryReadNext(CancellationToken.None) is { Success: true } res) {
			var rec = _records[count1];
			Assert.AreEqual(rec, res.LogRecord);
			Assert.AreEqual(rec.LogPosition, res.RecordPrePosition);
			Assert.AreEqual(rec.LogPosition + rec.GetSizeWithLengthPrefixAndSuffix(), res.RecordPostPosition);

			++count1;
		}

		Assert.AreEqual(RecordsCount, count1);

		int count2 = 0;
		while (await seqReader.TryReadPrev(CancellationToken.None) is { Success: true } res) {
			var rec = _records[RecordsCount - count2 - 1];
			Assert.AreEqual(rec, res.LogRecord);
			Assert.AreEqual(rec.LogPosition, res.RecordPrePosition);
			Assert.AreEqual(rec.LogPosition + rec.GetSizeWithLengthPrefixAndSuffix(), res.RecordPostPosition);

			++count2;
		}

		Assert.AreEqual(RecordsCount, count2);
	}

	[Test]
	public async Task records_can_be_read_forward_starting_from_any_position() {
		for (int i = 0; i < RecordsCount; ++i) {
			var seqReader = new TFChunkReader(_db, _db.Config.WriterCheckpoint, _records[i].LogPosition);

			int count = 0;
			while (await seqReader.TryReadNext(CancellationToken.None) is {Success:true} res) {
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
	public async Task records_can_be_read_backward_starting_from_any_position() {
		for (int i = 0; i < RecordsCount; ++i) {
			var seqReader = new TFChunkReader(_db, _db.Config.WriterCheckpoint, _records[i].LogPosition);

			SeqReadResult res;
			int count = 0;
			while ((res = await seqReader.TryReadPrev(CancellationToken.None)).Success) {
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
