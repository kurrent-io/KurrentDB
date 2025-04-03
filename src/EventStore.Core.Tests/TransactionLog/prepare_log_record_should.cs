// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using DotNext.Buffers;
using EventStore.Core.LogAbstraction;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.TransactionLog;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class prepare_log_record_should<TLogFormat, TStreamId> {
	private readonly IRecordFactory<TStreamId> _recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
	private readonly TStreamId _streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;

	public prepare_log_record_should() {
	}

	[Test]
	public void throw_argumentoutofrangeexception_when_given_negative_logposition() {
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		Assert.Throws<ArgumentOutOfRangeException>(() => {
			LogRecord.Prepare(_recordFactory, -1, Guid.NewGuid(), Guid.NewGuid(), 0, 0, _streamId, 0,
				PrepareFlags.None, eventTypeId, new byte[0], null, DateTime.UtcNow);
		});
	}

	[Test]
	public void throw_argumentoutofrangeexception_when_given_negative_transactionposition() {
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		Assert.Throws<ArgumentOutOfRangeException>(() => {
			LogRecord.Prepare(_recordFactory, 0, Guid.NewGuid(), Guid.NewGuid(), -1, 0, _streamId, 0,
				PrepareFlags.None, eventTypeId, new byte[0], null, DateTime.UtcNow);
		});
	}

	[Test]
	public void throw_argumentoutofrangeexception_when_given_transaction_offset_less_than_minus_one() {
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		Assert.Throws<ArgumentOutOfRangeException>(() => {
			LogRecord.Prepare(_recordFactory, 0, Guid.NewGuid(), Guid.NewGuid(), 0, -2, _streamId, 0,
				PrepareFlags.None, eventTypeId, new byte[0], null, DateTime.UtcNow);
		});
	}

	[Test]
	public void throw_argumentexception_when_given_empty_correlationid() {
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		Assert.Throws<ArgumentException>(() => {
			LogRecord.Prepare(_recordFactory, 0, Guid.Empty, Guid.NewGuid(), 0, 0, _streamId, 0,
				PrepareFlags.None, eventTypeId, new byte[0], null, DateTime.UtcNow);
		});
	}

	[Test]
	public void throw_argumentexception_when_given_empty_eventid() {
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		Assert.Throws<ArgumentException>(() => {
			LogRecord.Prepare(_recordFactory, 0, Guid.NewGuid(), Guid.Empty, 0, 0, _streamId, 0,
				PrepareFlags.None, eventTypeId, new byte[0], null, DateTime.UtcNow);
		});
	}

	[Test]
	public void throw_argumentnullexception_when_given_null_eventstreamid() {
		TStreamId nullStreamId = default;
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		var expectedExceptionType = LogFormatHelper<TLogFormat, TStreamId>.Choose<Type>(
			typeof(ArgumentNullException),
			typeof(ArgumentOutOfRangeException));

		Assert.Throws(expectedExceptionType, () => {
			LogRecord.Prepare(_recordFactory, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0, nullStreamId, 0,
				PrepareFlags.None, eventTypeId, new byte[0], null, DateTime.UtcNow);
		});
	}

	[Test]
	public void throw_argumentexception_when_given_empty_eventstreamid() {
		var emptyStreamId = LogFormatHelper<TLogFormat, TStreamId>.EmptyStreamId;
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		var expectedExceptionType = LogFormatHelper<TLogFormat, TStreamId>.Choose<Type>(
			typeof(ArgumentNullException),
			typeof(ArgumentOutOfRangeException));

		Assert.Throws(expectedExceptionType, () => {
			LogRecord.Prepare(_recordFactory, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0, emptyStreamId, 0,
				PrepareFlags.None, eventTypeId, new byte[0], null, DateTime.UtcNow);
		});
	}

	[Test]
	public void throw_argumentoutofrangeexception_when_given_incorrect_expectedversion() {
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		Assert.Throws<ArgumentOutOfRangeException>(() => {
			LogRecord.Prepare(_recordFactory, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0, _streamId, -3,
				PrepareFlags.None, eventTypeId, new byte[0], null, DateTime.UtcNow);
		});
	}

	[Test, Ignore("ReadOnlyMemory will always convert back to empty array if initialized with null array.")]
	public void throw_argumentnullexception_when_given_null_data() {
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		Assert.Throws<ArgumentNullException>(() => {
			LogRecord.Prepare(_recordFactory, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0, _streamId, 0,
				PrepareFlags.None, eventTypeId, null, null, DateTime.UtcNow);
		});
	}

	[Test]
	public void throw_argumentnullexception_when_given_null_eventtype() {
		Assert.DoesNotThrow(() =>
			LogRecord.Prepare(_recordFactory, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0, _streamId, 0,
				PrepareFlags.None, default, new byte[0], null, DateTime.UtcNow));
	}

	[Test]
	public void throw_argumentexception_when_given_empty_eventtype() {
		var emptyEventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		Assert.DoesNotThrow(() =>
			LogRecord.Prepare(_recordFactory, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0, _streamId, 0,
				PrepareFlags.None, emptyEventTypeId, new byte[0], null,  DateTime.UtcNow));
	}

	[Test]
	public void return_empty_data_when_event_is_redacted() {
		if (typeof(TLogFormat) == typeof(LogFormat.V3)) {
			Assert.Ignore("Log V3 does not handle redacted events yet");
			return;
		}

		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;

		var prepare = LogRecord.Prepare(_recordFactory, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0, _streamId, 0,
			PrepareFlags.IsRedacted, eventTypeId, new byte[100], null,  DateTime.UtcNow);
		Assert.AreEqual(0, prepare.Data.Length);
	}

	[Test]
	public void write_redacted_data_when_event_is_redacted() {
		if (typeof(TLogFormat) == typeof(LogFormat.V3)) {
			Assert.Ignore("Log V3 does not handle redacted events yet");
			return;
		}

		var binaryWriter = new BufferWriterSlim<byte>();

		const int dataSize = 10000;
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;
		var prepare = LogRecord.Prepare(_recordFactory, 0, Guid.NewGuid(), Guid.NewGuid(), 0, 0, _streamId, 0,
			PrepareFlags.IsRedacted, eventTypeId, new byte[dataSize], null,  DateTime.UtcNow);

		prepare.WriteTo(ref binaryWriter);
		Assert.True(binaryWriter.WrittenCount >= dataSize);
	}
}
