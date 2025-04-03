// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.ClientAPI;
using NUnit.Framework;
using EventStore.Core.Data;

namespace EventStore.Core.Tests.ClientAPI.ExpectedVersion64Bit;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint), Ignore = "Explicit transactions are not supported yet by Log V3")]
[Category("ClientAPI"), Category("LongRunning")]
public class transactions_on_stream_with_event_numbers_greater_than_2_billion<TLogFormat, TStreamId>
	: MiniNodeWithExistingRecords<TLogFormat, TStreamId> {
	private const string StreamName = "transactions_on_stream_with_event_numbers_greater_than_2_billion";
	private const long intMaxValue = (long)int.MaxValue;

	private EventRecord _r1, _r2, _r3, _r4, _r5;

	public override async ValueTask WriteTestScenario(CancellationToken token) {
		_r1 = await WriteSingleEvent(StreamName, intMaxValue + 1, new string('.', 3000), token: token);
		_r2 = await WriteSingleEvent(StreamName, intMaxValue + 2, new string('.', 3000), token: token);
		_r3 = await WriteSingleEvent(StreamName, intMaxValue + 3, new string('.', 3000), token: token);
		_r4 = await WriteSingleEvent(StreamName, intMaxValue + 4, new string('.', 3000), token: token);
		_r5 = await WriteSingleEvent(StreamName, intMaxValue + 5, new string('.', 3000), token: token);
	}

	public override async Task Given() {
		_store = BuildConnection(Node);
		await _store.ConnectAsync();
		await _store.SetStreamMetadataAsync(StreamName, EventStore.ClientAPI.ExpectedVersion.Any,
			EventStore.ClientAPI.StreamMetadata.Create(truncateBefore: intMaxValue + 1));
	}

	[Test]
	public async Task should_be_able_to_append_to_stream_in_a_transaction() {
		var evnt1 = new EventData(Guid.NewGuid(), "EventType", false, new byte[10], new byte[15]);
		var evnt2 = new EventData(Guid.NewGuid(), "EventType", false, new byte[10], new byte[15]);

		var transaction = await _store.StartTransactionAsync(StreamName, intMaxValue + 5, DefaultData.AdminCredentials)
;
		await transaction.WriteAsync(evnt1);
		await transaction.WriteAsync(evnt2);
		await transaction.CommitAsync();

		var records = await _store.ReadStreamEventsForwardAsync(StreamName, intMaxValue, 10, false);
		Assert.AreEqual(7, records.Events.Length);
		Assert.AreEqual(_r1.EventId, records.Events[0].Event.EventId);
		Assert.AreEqual(_r2.EventId, records.Events[1].Event.EventId);
		Assert.AreEqual(_r3.EventId, records.Events[2].Event.EventId);
		Assert.AreEqual(_r4.EventId, records.Events[3].Event.EventId);
		Assert.AreEqual(_r5.EventId, records.Events[4].Event.EventId);
		Assert.AreEqual(evnt1.EventId, records.Events[5].Event.EventId);
		Assert.AreEqual(evnt2.EventId, records.Events[6].Event.EventId);
	}
}
