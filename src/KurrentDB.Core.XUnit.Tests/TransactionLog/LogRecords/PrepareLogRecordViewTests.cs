// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using DotNext.Buffers;
using KurrentDB.Core.TransactionLog.LogRecords;
using Xunit;

namespace KurrentDB.Core.XUnit.Tests.TransactionLog.LogRecords;

public class PrepareLogRecordViewTests {
	private const long LogPosition = 123;
	private readonly Guid _correlationId = Guid.NewGuid();
	private readonly Guid _eventId = Guid.NewGuid();
	private const long TransactionPosition = 456;
	private const int TransactionOffset = 321;
	private const string EventStreamId = "test_stream";
	private const long ExpectedVersion = 789;
	private readonly DateTime _timestamp = DateTime.UtcNow;
	private const string EventType = "test_event_type";
	private readonly byte[] _data = { 0xDE, 0XAD, 0xC0, 0XDE };
	private readonly byte[] _metadata = { 0XC0, 0xDE };
	private const byte Version = 1;

	private PrepareLogRecord CreatePrepareLogRecord() {
		return new PrepareLogRecord(
			LogPosition,
			_correlationId,
			_eventId,
			TransactionPosition,
			TransactionOffset,
			EventStreamId,
			null,
			ExpectedVersion,
			_timestamp,
			PrepareFlags.SingleWrite,
			EventType,
			null,
			_data,
			_metadata,
			Version);
	}

	[Fact]
	public void should_have_correct_properties() {
		var prepareLogRecord = CreatePrepareLogRecord();
		var writer = new BufferWriterSlim<byte>();
		prepareLogRecord.WriteTo(ref writer);

		using var recordBuffer = writer.DetachOrCopyBuffer();
		var record = recordBuffer.Memory.ToArray();

		var prepare = new PrepareLogRecordView(record, record.Length);

		Assert.Equal(LogPosition, prepare.LogPosition);
		Assert.Equal(_correlationId, prepare.CorrelationId);
		Assert.Equal(_eventId, prepare.EventId);
		Assert.Equal(TransactionPosition, prepare.TransactionPosition);
		Assert.Equal(TransactionOffset, prepare.TransactionOffset);
		Assert.True(prepare.EventStreamId.SequenceEqual(Encoding.UTF8.GetBytes(EventStreamId)));
		Assert.Equal(ExpectedVersion, prepare.ExpectedVersion);
		Assert.Equal(_timestamp, prepare.TimeStamp);
		Assert.Equal(PrepareFlags.SingleWrite, prepare.Flags);
		Assert.True(prepare.Data.SequenceEqual(_data));
		Assert.True(prepare.Metadata.SequenceEqual(_metadata));
		Assert.Equal(Version, prepare.Version);
	}
}
