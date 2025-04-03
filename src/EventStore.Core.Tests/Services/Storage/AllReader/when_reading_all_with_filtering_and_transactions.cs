// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Data;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Tests.TransactionLog.Scavenging.Helpers;
using NUnit.Framework;

namespace EventStore.Core.Tests.Services.Storage.AllReader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "Explicit transactions are not supported yet by Log V3")]
public class when_reading_all_with_filtering_and_transactions<TLogFormat, TStreamId>
	: RepeatableDbTestScenario<TLogFormat, TStreamId> {

	[Test]
	public async Task should_receive_all_events_forward() {
		// create a db with explicit transactions, some of which are filtered out on read.
		// previously, a bug caused those filtered-out records to prevent the successful
		// reading of subsequent events that are contained within an explicit transaction.

		static Rec[] ExplicitTransaction(int transaction, string stream) => [
			Rec.TransSt(transaction, stream),
			Rec.Prepare(transaction, stream),
			Rec.TransEnd(transaction, stream),
			Rec.Commit(transaction, stream),
		];

		var i = 0;
		await CreateDb([
			.. ExplicitTransaction(i++, "excludedStream"),
			.. ExplicitTransaction(i++, "includedStream0"),
			.. ExplicitTransaction(i++, "includedStream1"),
			.. ExplicitTransaction(i++, "includedStream2"),
			.. ExplicitTransaction(i++, "includedStream3"),
			.. ExplicitTransaction(i++, "includedStream4"),
			.. ExplicitTransaction(i++, "includedStream5"),
			.. ExplicitTransaction(i++, "includedStream6"),
			.. ExplicitTransaction(i++, "includedStream7"),
			.. ExplicitTransaction(i++, "includedStream8"),
			.. ExplicitTransaction(i++, "includedStream9"),
		]);

		var read = await ReadIndex.ReadAllEventsForwardFiltered(
			pos: new Data.TFPos(0, 0),
			maxCount: 10,
			maxSearchWindow: int.MaxValue,
			eventFilter: EventFilter.StreamName.Prefixes(false, "included"),
			CancellationToken.None);

		Assert.AreEqual(10, read.Records.Count);
		for (int j = 0; j < 10; j++)
			Assert.AreEqual($"includedStream{j}", read.Records[j].Event.EventStreamId);
	}

	[Test]
	public async Task should_receive_all_events_backward() {
		// create a db with explicit transactions, some of which are filtered out on read.
		// previously, a bug caused those filtered-out records to prevent the successful
		// reading of subsequent events that are contained within an explicit transaction.

		static Rec[] ExplicitTransaction(int transaction, string stream) => [
			Rec.TransSt(transaction, stream),
			Rec.Prepare(transaction, stream),
			Rec.TransEnd(transaction, stream),
			Rec.Commit(transaction, stream),
		];

		var i = 0;
		await CreateDb([
			.. ExplicitTransaction(i++, "includedStream0"),
			.. ExplicitTransaction(i++, "includedStream1"),
			.. ExplicitTransaction(i++, "includedStream2"),
			.. ExplicitTransaction(i++, "includedStream3"),
			.. ExplicitTransaction(i++, "includedStream4"),
			.. ExplicitTransaction(i++, "includedStream5"),
			.. ExplicitTransaction(i++, "includedStream6"),
			.. ExplicitTransaction(i++, "includedStream7"),
			.. ExplicitTransaction(i++, "includedStream8"),
			.. ExplicitTransaction(i++, "includedStream9"),
			.. ExplicitTransaction(i++, "excludedStream"),
		]);

		var writerCp = DbRes.Db.Config.WriterCheckpoint.Read();
		var read = await ReadIndex.ReadAllEventsBackwardFiltered(
			pos: new TFPos(writerCp, writerCp),
			maxCount: 10,
			maxSearchWindow: int.MaxValue,
			eventFilter: EventFilter.StreamName.Prefixes(false, "included"),
			CancellationToken.None);

		Assert.AreEqual(10, read.Records.Count);
		for (int j = 9; j <= 0; j--)
			Assert.AreEqual($"includedStream{j}", read.Records[j].Event.EventStreamId);
	}
}
