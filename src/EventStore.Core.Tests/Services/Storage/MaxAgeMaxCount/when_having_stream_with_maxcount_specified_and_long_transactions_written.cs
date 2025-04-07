// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.MaxAgeMaxCount;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "Explicit transactions are not supported yet by Log V3")]
public class when_having_stream_with_maxcount_specified_and_long_transactions_written<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord[] _records;

	protected override void WriteTestScenario() {
		const string metadata = @"{""$maxCount"":2}";

		_records = new EventRecord[9]; // 3 + 2 + 4
		WriteStreamMetadata("ES", 0, metadata);

		WriteTransaction(-1, 3);
		WriteTransaction(2, 2);
		WriteTransaction(-1 + 3 + 2, 4);
	}

	private void WriteTransaction(long expectedVersion, int transactionLength) {
		var begin = WriteTransactionBegin("ES", expectedVersion);
		for (int i = 0; i < transactionLength; ++i) {
			var eventNumber = expectedVersion + i + 1;
			_records[eventNumber] = WriteTransactionEvent(Guid.NewGuid(), begin.LogPosition, i, "ES", eventNumber,
				"data" + i, PrepareFlags.Data);
		}

		WriteTransactionEnd(Guid.NewGuid(), begin.LogPosition, "ES");
		WriteCommit(Guid.NewGuid(), begin.LogPosition, "ES", expectedVersion + 1);
	}

	[Test]
	public void forward_range_read_returns_last_transaction_events_and_doesnt_return_expired_ones() {
		var result = ReadIndex.ReadStreamEventsForward("ES", 0, 100);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_records[7], result.Records[0]);
		Assert.AreEqual(_records[8], result.Records[1]);
	}
}
