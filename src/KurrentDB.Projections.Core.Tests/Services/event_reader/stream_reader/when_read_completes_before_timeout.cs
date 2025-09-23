// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.Services.TimeService;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.SingleStream;
using KurrentDB.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;
using ReadStreamResult = KurrentDB.Core.Data.ReadStreamResult;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader.stream_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_read_completes_before_timeout<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private StreamEventReader _eventReader;
	private Guid _distributionCorrelationId;
	private FakeTimeProvider _fakeTimeProvider;

	protected override void Given() {
		TicksAreHandledImmediately();
	}

	[SetUp]
	public new void When() {
		_distributionCorrelationId = Guid.NewGuid();
		_fakeTimeProvider = new();
		_eventReader = new(_bus, _distributionCorrelationId, null, "stream", 10, _fakeTimeProvider, resolveLinkTos: false, stopOnEof: true,
			produceStreamDeletes: false);
		_eventReader.Resume();
		var correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Last().CorrelationId;
		_eventReader.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				correlationId, "stream", 100, 100, ReadStreamResult.Success,
				[
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							10, 50, Guid.NewGuid(), Guid.NewGuid(), 50, 0, "stream", ExpectedVersion.Any,
							DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type1", [1], [2])),
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							11, 100, Guid.NewGuid(), Guid.NewGuid(), 100, 0, "stream", ExpectedVersion.Any,
							DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type2", [3], [4]))
				], null, false, "", 12, 11, true, 200));
		_eventReader.Handle(new ProjectionManagementMessage.Internal.ReadTimeout(correlationId, "stream"));
	}

	[Test]
	public void should_deliver_events() {
		Assert.AreEqual(2, _consumer.HandledMessages.OfType<ReaderSubscriptionMessage.CommittedEventDistributed>().Count());
	}
}
