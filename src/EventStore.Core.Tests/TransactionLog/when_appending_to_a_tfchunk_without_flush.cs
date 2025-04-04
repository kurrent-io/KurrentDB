// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_appending_to_a_tfchunk_without_flush<TLogFormat, TStreamId> : SpecificationWithFilePerTestFixture {
	private TFChunk _chunk;
	private readonly Guid _corrId = Guid.NewGuid();
	private readonly Guid _eventId = Guid.NewGuid();
	private RecordWriteResult _result;
	private IPrepareLogRecord<TStreamId> _record;

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
	}

	[OneTimeTearDown]
	public override void TestFixtureTearDown() {
		_chunk.Dispose();
		base.TestFixtureTearDown();
	}

	[Test]
	public void the_record_is_appended() {
		Assert.IsTrue(_result.Success);
	}

	[Test]
	public void the_old_position_is_returned() {
		//position without header.
		Assert.AreEqual(0, _result.OldPosition);
	}

	[Test]
	public void the_updated_position_is_returned() {
		//position without header.
		Assert.AreEqual(_record.GetSizeWithLengthPrefixAndSuffix(), _result.NewPosition);
	}
}
