// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using DotNext.Buffers;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.LogCommon;
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
	private readonly DateTime _timestamp = DateTime.Now;
	private const string EventType = "test_event_type";
	private readonly byte[] _data = { 0xDE, 0XAD, 0xC0, 0XDE };

	private PrepareLogRecord CreatePrepareLogRecord(byte version, byte[] metadata, byte[] properties) {
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
			metadata,
			properties,
			version);
	}

	[Theory]
	[InlineData(PrepareLogRecordVersion.V1, new byte[] { 0XC0, 0xDE }, new byte[] { })]
	[InlineData(PrepareLogRecordVersion.V2, new byte[] { }, new byte[] { 0xDE, 0XAD })]
	public void should_have_correct_properties(byte expectedVersion, byte[] metadata, byte[] properties) {
		var prepareLogRecord = CreatePrepareLogRecord(expectedVersion, metadata, properties);
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
		Assert.True(prepare.Metadata.SequenceEqual(metadata));
		Assert.True(prepare.Properties.SequenceEqual(properties));
		Assert.Equal(expectedVersion, prepare.Version);
	}
}
