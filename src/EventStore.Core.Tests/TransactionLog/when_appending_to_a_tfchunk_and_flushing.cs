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
public class when_appending_to_a_tfchunk_and_flushing<TLogFormat, TStreamId> : SpecificationWithFilePerTestFixture {
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
		await _chunk.Flush(CancellationToken.None);
	}

	[OneTimeTearDown]
	public override void TestFixtureTearDown() {
		_chunk.Dispose();
		base.TestFixtureTearDown();
	}

	[Test]
	public void the_write_result_is_correct() {
		Assert.IsTrue(_result.Success);
		Assert.AreEqual(0, _result.OldPosition);
		Assert.AreEqual(_record.GetSizeWithLengthPrefixAndSuffix(), _result.NewPosition);
	}

	[Test]
	public void the_record_is_appended() {
		Assert.IsTrue(_result.Success);
	}

	[Test]
	public void correct_old_position_is_returned() {
		//position without header (logical position).
		Assert.AreEqual(0, _result.OldPosition);
	}

	[Test]
	public void the_updated_position_is_returned() {
		//position without header (logical position).
		Assert.AreEqual(_record.GetSizeWithLengthPrefixAndSuffix(), _result.NewPosition);
	}

	[Test]
	public async Task the_record_can_be_read_at_exact_position() {
		var res = await _chunk.TryReadAt(0, couldBeScavenged: false, CancellationToken.None);
		Assert.IsTrue(res.Success);
		Assert.AreEqual(_record, res.LogRecord);
		Assert.AreEqual(_result.OldPosition, res.LogRecord.LogPosition);
	}

	[Test]
	public async Task the_record_can_be_read_as_first_one() {
		var res = await _chunk.TryReadFirst(CancellationToken.None);
		Assert.IsTrue(res.Success);
		Assert.AreEqual(_record, res.LogRecord);
		Assert.AreEqual(_record.GetSizeWithLengthPrefixAndSuffix(), res.NextPosition);
	}

	[Test]
	public async Task the_record_can_be_read_as_closest_forward_to_pos_zero() {
		var res = await _chunk.TryReadClosestForward(0, CancellationToken.None);
		Assert.IsTrue(res.Success);
		Assert.AreEqual(_record, res.LogRecord);
		Assert.AreEqual(_record.GetSizeWithLengthPrefixAndSuffix(), res.NextPosition);
	}

	[Test]
	public async Task the_record_can_be_read_as_closest_backward_from_end() {
		var res = await _chunk.TryReadClosestBackward(_record.GetSizeWithLengthPrefixAndSuffix(), CancellationToken.None);
		Assert.IsTrue(res.Success);
		Assert.AreEqual(_record, res.LogRecord);
		Assert.AreEqual(0, res.NextPosition);
	}

	[Test]
	public async Task the_record_can_be_read_as_last_one() {
		var res = await _chunk.TryReadLast(CancellationToken.None);
		Assert.IsTrue(res.Success);
		Assert.AreEqual(_record, res.LogRecord);
		Assert.AreEqual(0, res.NextPosition);
	}
}
