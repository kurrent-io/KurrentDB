// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Text;
using DotNext.Buffers;
using EventStore.Core.TransactionLog.LogRecords;
using Xunit;

namespace EventStore.Core.XUnit.Tests.TransactionLog.LogRecords;

public class PrepareLogRecordViewTests {
	private PrepareLogRecordView _prepare;
	private const long LogPosition = 123;
	private readonly Guid _correlationId = Guid.NewGuid();
	private readonly Guid _eventId = Guid.NewGuid();
	private const long TransactionPosition = 456;
	private const int TransactionOffset = 321;
	private const string EventStreamId = "test_stream";
	private const long ExpectedVersion = 789;
	private readonly DateTime _timestamp = DateTime.Now;
	private const PrepareFlags Flags = PrepareFlags.SingleWrite;
	private const string EventType = "test_event_type";
	private readonly byte[] _data = { 0xDE, 0XAD, 0xC0, 0XDE };
	private readonly byte[] _metadata = { 0XC0, 0xDE };
	private const byte Version = 1;

	public PrepareLogRecordViewTests() {
		var prepare = new PrepareLogRecord(
			LogPosition,
			_correlationId,
			_eventId,
			TransactionPosition,
			TransactionOffset,
			EventStreamId,
			null,
			ExpectedVersion,
			_timestamp,
			Flags,
			EventType,
			null,
			_data,
			_metadata,
			Version);

		var writer = new BufferWriterSlim<byte>();
		prepare.WriteTo(ref writer);

		using var recordBuffer = writer.DetachOrCopyBuffer();
		var record = recordBuffer.Memory.ToArray();

		_prepare = new PrepareLogRecordView(record, record.Length);
	}

	[Fact]
	public void should_have_correct_properties() {
		Assert.Equal(LogPosition, _prepare.LogPosition);
		Assert.Equal(_correlationId, _prepare.CorrelationId);
		Assert.Equal(_eventId, _prepare.EventId);
		Assert.Equal(TransactionPosition, _prepare.TransactionPosition);
		Assert.Equal(TransactionOffset, _prepare.TransactionOffset);
		Assert.True(_prepare.EventStreamId.SequenceEqual(Encoding.UTF8.GetBytes(EventStreamId)));
		Assert.Equal(ExpectedVersion, _prepare.ExpectedVersion);
		Assert.Equal(_timestamp, _prepare.TimeStamp);
		Assert.Equal(Flags, _prepare.Flags);
		Assert.True(_prepare.Data.SequenceEqual(_data));
		Assert.True(_prepare.Metadata.SequenceEqual(_metadata));
		Assert.Equal(Version, _prepare.Version);
	}
}
