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
public class
	when_multiple_single_writes_are_after_transaction_end_but_before_commit_is_present<TLogFormat, TStreamId>
	: RepeatableDbTestScenario<TLogFormat, TStreamId> {
	[Test]
	public async Task should_be_able_to_read_the_transactional_writes_when_the_commit_is_present() {
		/*
		 * create a db with a transaction where the commit is not present yet (read happened before the chaser could commit)
		 * in the following case the read will return the event for the single non-transactional write
		 * performing a read from the next position returned will fail as the prepares are all less than what we have asked for.
		 */
		await CreateDb([
			Rec.TransSt(0, "transaction_stream_id"),
			Rec.Prepare(0, "transaction_stream_id"),
			Rec.TransEnd(0, "transaction_stream_id"),
			Rec.Prepare(1, "single_write_stream_id_1", prepareFlags: PrepareFlags.SingleWrite | PrepareFlags.IsCommitted),
			Rec.Prepare(2, "single_write_stream_id_2", prepareFlags: PrepareFlags.SingleWrite | PrepareFlags.IsCommitted),
			Rec.Prepare(3, "single_write_stream_id_3", prepareFlags: PrepareFlags.SingleWrite | PrepareFlags.IsCommitted)
			]);

		var firstRead = await ReadIndex.ReadAllEventsForward(new Data.TFPos(0, 0), 10, CancellationToken.None);

		Assert.AreEqual(3, firstRead.Records.Count);
		Assert.AreEqual("single_write_stream_id_1", firstRead.Records[0].Event.EventStreamId);
		Assert.AreEqual("single_write_stream_id_2", firstRead.Records[1].Event.EventStreamId);
		Assert.AreEqual("single_write_stream_id_3", firstRead.Records[2].Event.EventStreamId);

		//create the exact same db as above but now with the transaction's commit
		await CreateDb([
			Rec.TransSt(0, "transaction_stream_id"),
			Rec.Prepare(0, "transaction_stream_id"),
			Rec.TransEnd(0, "transaction_stream_id"),
			Rec.Prepare(1, "single_write_stream_id_1", prepareFlags: PrepareFlags.SingleWrite | PrepareFlags.IsCommitted),
			Rec.Prepare(2, "single_write_stream_id_2", prepareFlags: PrepareFlags.SingleWrite | PrepareFlags.IsCommitted),
			Rec.Prepare(3, "single_write_stream_id_3", prepareFlags: PrepareFlags.SingleWrite | PrepareFlags.IsCommitted),
			Rec.Commit(0, "transaction_stream_id")
			]);

		var transactionRead = await ReadIndex.ReadAllEventsForward(firstRead.NextPos, 10, CancellationToken.None);

		Assert.AreEqual(1, transactionRead.Records.Count);
		Assert.AreEqual("transaction_stream_id", transactionRead.Records[0].Event.EventStreamId);
	}
}
