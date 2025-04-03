// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Services.Storage.ReaderIndex.ReadStreamResult;

namespace EventStore.Core.Tests.Services.Storage.MaxAgeMaxCount;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "Explicit transactions are not supported yet by Log V3")]
public class when_having_stream_with_maxcount_specified_and_long_transactions_written<TLogFormat, TStreamId> : ReadIndexTestScenario<TLogFormat, TStreamId> {
	private EventRecord[] _records;

	protected override async ValueTask WriteTestScenario(CancellationToken token) {
		const string metadata = @"{""$maxCount"":2}";

		_records = new EventRecord[9]; // 3 + 2 + 4
		await WriteStreamMetadata("ES", 0, metadata, token: token);

		await WriteTransaction(-1, 3, token);
		await WriteTransaction(2, 2, token);
		await WriteTransaction(-1 + 3 + 2, 4, token);
	}

	private async ValueTask WriteTransaction(long expectedVersion, int transactionLength, CancellationToken token) {
		var begin = await WriteTransactionBegin("ES", expectedVersion, token);
		for (int i = 0; i < transactionLength; ++i) {
			var eventNumber = expectedVersion + i + 1;
			_records[eventNumber] = await WriteTransactionEvent(Guid.NewGuid(), begin.LogPosition, i, "ES", eventNumber,
				"data" + i, PrepareFlags.Data, token: token);
		}

		await WriteTransactionEnd(Guid.NewGuid(), begin.LogPosition, "ES", token);
		await WriteCommit(Guid.NewGuid(), begin.LogPosition, "ES", expectedVersion + 1, token);
	}

	[Test]
	public async Task forward_range_read_returns_last_transaction_events_and_doesnt_return_expired_ones() {
		var result = await ReadIndex.ReadStreamEventsForward("ES", 0, 100, CancellationToken.None);
		Assert.AreEqual(ReadStreamResult.Success, result.Result);
		Assert.AreEqual(2, result.Records.Length);
		Assert.AreEqual(_records[7], result.Records[0]);
		Assert.AreEqual(_records[8], result.Records[1]);
	}
}
