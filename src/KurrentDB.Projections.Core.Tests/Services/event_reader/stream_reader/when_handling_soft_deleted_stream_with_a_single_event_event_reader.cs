// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Tests;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.SingleStream;
using KurrentDB.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader.stream_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_handling_soft_deleted_stream_with_a_single_event_event_reader<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private StreamEventReader _streamEventReader;
	private Guid _distributionPointCorrelationId;
	private Guid _firstEventId;
	private Guid _secondEventId;
	private string _streamId = Guid.NewGuid().ToString();

	protected override void Given() {
		TicksAreHandledImmediately();

		_distributionPointCorrelationId = Guid.NewGuid();
		_streamEventReader = new(_bus, _distributionPointCorrelationId, null, _streamId, 0, new RealTimeProvider(), false, produceStreamDeletes: false);
		_streamEventReader.Resume();
		_firstEventId = Guid.NewGuid();
		_secondEventId = Guid.NewGuid();
	}

	[SetUp]
	public new void When() {
		var correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Last().CorrelationId;
		_streamEventReader.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				correlationId, _streamId, 100, 100, ReadStreamResult.Success,
				[
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							10, 50, Guid.NewGuid(), _firstEventId, 50, 0, _streamId, ExpectedVersion.Any,
							DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type1", [1], [2])),
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							11, 100, Guid.NewGuid(), _secondEventId, 100, 0, _streamId, ExpectedVersion.Any,
							DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd |
							PrepareFlags.IsJson,
							"event_type2", [3], [4]))
				], null, false, "", 12, 11, true, 200));
	}

	[Test]
	public void should_handle_the_2_events() {
		Assert.AreEqual(2, _consumer.HandledMessages.OfType<ReaderSubscriptionMessage.CommittedEventDistributed>().Count());

		var first = _consumer.HandledMessages.OfType<ReaderSubscriptionMessage.CommittedEventDistributed>().First();
		Assert.AreEqual(first.Data.EventId, _firstEventId, $"Expected the first event to be {_firstEventId}, but got {first.Data.EventId}");
		var second = _consumer.HandledMessages.OfType<ReaderSubscriptionMessage.CommittedEventDistributed>().Skip(1).First();
		Assert.AreEqual(second.Data.EventId, _secondEventId, $"Expected the second event to be {_secondEventId}, but got {second.Data.EventId}");
	}
}
