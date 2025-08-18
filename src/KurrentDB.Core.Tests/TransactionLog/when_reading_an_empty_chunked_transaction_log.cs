// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.TransactionLog;
using KurrentDB.Core.TransactionLog.Checkpoint;
using KurrentDB.Core.TransactionLog.Chunks;
using KurrentDB.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.TransactionLog;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_reading_an_empty_chunked_transaction_log<TLogFormat, TStreamId> : SpecificationWithDirectory {
	[Test]
	public async Task try_read_returns_false_when_writer_checksum_is_zero() {
		var writerchk = new InMemoryCheckpoint(0);
		var chaserchk = new InMemoryCheckpoint(0);
		await using var db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, writerchk, chaserchk));
		await db.Open();

		var reader = new TFChunkReader(db, writerchk);
		using var cursorScope = new AsyncReadCursor.Scope();
		Assert.IsTrue(await reader.TryReadNext<AsyncReadCursor>(cursorScope, CancellationToken.None) is { Success: false });
	}

	[Test]
	public async Task try_read_does_not_cache_anything_and_returns_record_once_it_is_written_later() {
		var writerchk = new InMemoryCheckpoint(0);
		var chaserchk = new InMemoryCheckpoint(0);
		await using var db = new TFChunkDb(TFChunkHelper.CreateDbConfig(PathName, writerchk, chaserchk));
		await db.Open();

		var writer = new TFChunkWriter(db);
		await writer.Open(CancellationToken.None);

		var reader = new TFChunkReader(db, writerchk);
		using var cursorScope = new AsyncReadCursor.Scope();

		Assert.IsTrue(await reader.TryReadNext<AsyncReadCursor>(cursorScope, CancellationToken.None) is { Success: false });

		var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
		var streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		var rec = LogRecord.SingleWrite(recordFactory, 0, Guid.NewGuid(), Guid.NewGuid(), streamId, -1, eventTypeId, new byte[] { 7 }, null);

		Assert.IsTrue(await writer.Write(rec, CancellationToken.None) is (true, _));
		await writer.DisposeAsync();

		var res = await reader.TryReadNext<AsyncReadCursor>(cursorScope, CancellationToken.None);
		Assert.IsTrue(res.Success);
		Assert.AreEqual(rec, res.LogRecord);
	}
}
