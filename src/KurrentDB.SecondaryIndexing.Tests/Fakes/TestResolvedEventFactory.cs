// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Text;
using KurrentDB.Core.Data;
using KurrentDB.Core.Tests;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.SecondaryIndexing.Tests.Fakes;

public static class TestResolvedEventFactory {
	public static ResolvedEvent From(
		string streamName,
		int streamPosition,
		long logPosition,
		string eventType,
		byte[] data
	) {
		var recordFactory = LogFormatHelper<LogFormat.V2, string>.RecordFactory;
		var streamIdIgnored = LogFormatHelper<LogFormat.V2, string>.StreamId;
		var eventTypeIdIgnored = LogFormatHelper<LogFormat.V2, string>.EventTypeId;

		var record = new EventRecord(
			streamPosition,
			LogRecord.Prepare(recordFactory, logPosition, Guid.NewGuid(), Guid.NewGuid(), 0, 0,
				streamIdIgnored, streamPosition, PrepareFlags.None, eventTypeIdIgnored, data,
				Encoding.UTF8.GetBytes("")
			),
			streamName,
			eventType
		);

		return ResolvedEvent.ForUnresolvedEvent(record, 0);
	}
}
