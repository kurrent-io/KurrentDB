// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using EventStore.Core.TransactionLog.LogRecords;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.AllReader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "Explicit transactions are not supported yet by Log V3")]
public class when_a_single_write_before_the_transaction_is_present<TLogFormat, TStreamId> : RepeatableDbTestScenario<TLogFormat, TStreamId> {
	[Test]
	public async Task should_be_able_to_read_the_transactional_writes_when_the_commit_is_present() {
		await CreateDb([
			Rec.Prepare(0, "single_write_stream_id_1", prepareFlags: PrepareFlags.Data | PrepareFlags.IsCommitted),
			Rec.TransSt(1, "transaction_stream_id"),
			Rec.Prepare(1, "transaction_stream_id"),
			Rec.TransEnd(1, "transaction_stream_id"),
			Rec.Prepare(2, "single_write_stream_id_2", prepareFlags: PrepareFlags.SingleWrite | PrepareFlags.IsCommitted),
			Rec.Prepare(3, "single_write_stream_id_3", prepareFlags: PrepareFlags.SingleWrite | PrepareFlags.IsCommitted),
			Rec.Prepare(4, "single_write_stream_id_4", prepareFlags: PrepareFlags.SingleWrite | PrepareFlags.IsCommitted)
			]);

		var firstRead = await ReadIndex.ReadAllEventsForward(new Data.TFPos(0, 0), 10, CancellationToken.None);

		Assert.AreEqual(4, firstRead.Records.Count);
		Assert.AreEqual("single_write_stream_id_1", firstRead.Records[0].Event.EventStreamId);
		Assert.AreEqual("single_write_stream_id_2", firstRead.Records[1].Event.EventStreamId);
		Assert.AreEqual("single_write_stream_id_3", firstRead.Records[2].Event.EventStreamId);
		Assert.AreEqual("single_write_stream_id_4", firstRead.Records[3].Event.EventStreamId);

		await CreateDb([
			Rec.Prepare(0, "single_write_stream_id_1", prepareFlags: PrepareFlags.Data | PrepareFlags.IsCommitted),
			Rec.TransSt(1, "transaction_stream_id"),
			Rec.Prepare(1, "transaction_stream_id"),
			Rec.TransEnd(1, "transaction_stream_id"),
			Rec.Prepare(2, "single_write_stream_id_2", prepareFlags: PrepareFlags.SingleWrite | PrepareFlags.IsCommitted),
			Rec.Prepare(3, "single_write_stream_id_3", prepareFlags: PrepareFlags.SingleWrite | PrepareFlags.IsCommitted),
			Rec.Prepare(4, "single_write_stream_id_4", prepareFlags: PrepareFlags.SingleWrite | PrepareFlags.IsCommitted),
			Rec.Commit(1, "transaction_stream_id")
			]);

		var transactionRead = await ReadIndex.ReadAllEventsForward(firstRead.NextPos, 10, CancellationToken.None);

		Assert.AreEqual(1, transactionRead.Records.Count);
		Assert.AreEqual("transaction_stream_id", transactionRead.Records[0].Event.EventStreamId);
	}
}
