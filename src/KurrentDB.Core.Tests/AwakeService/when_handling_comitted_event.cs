// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace KurrentDB.Core.Tests.AwakeService;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_handling_comitted_event<TLogFormat, TStreamId> {
	private KurrentDB.Core.Services.AwakeReaderService.AwakeService _it;
	private EventRecord _eventRecord;
	private StorageMessage.EventCommitted _eventCommitted;
	private Exception _exception;

	[SetUp]
	public void SetUp() {
		_exception = null;
		Given();
		When();
	}

	private void Given() {
		_it = new();

		var recordFactory = LogFormatHelper<TLogFormat, TStreamId>.RecordFactory;
		var streamId = LogFormatHelper<TLogFormat, TStreamId>.StreamId;
		var eventTypeId = LogFormatHelper<TLogFormat, TStreamId>.EventTypeId;

		_eventRecord = new EventRecord(
			10,
			recordFactory.Prepare(
				500, Guid.NewGuid(), Guid.NewGuid(), 500, 0, streamId, 99, PrepareFlags.Data,
				eventTypeId, [], null, DateTime.UtcNow), "Stream", "EventType");
		_eventCommitted = new(1000, _eventRecord, isTfEof: true);
	}

	private void When() {
		try {
			_it.Handle(_eventCommitted);
		} catch (Exception ex) {
			_exception = ex;
		}
	}

	[Test]
	public void it_is_handled() {
		Assert.IsNull(_exception, (_exception ?? (object)"").ToString());
	}
}
