// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Tests;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.MultiStream;
using KurrentDB.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader.multi_stream_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_read_completes_before_timeout<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private MultiStreamEventReader _eventReader;
	private Guid _distributionPointCorrelationId;

	protected override void Given() {
		TicksAreHandledImmediately();
	}

	private string[] _abStreams;
	private Dictionary<string, long> _ab12Tag;

	[SetUp]
	public new void When() {
		_ab12Tag = new() { { "a", 1 }, { "b", 2 } };
		_abStreams = ["a", "b"];
		_distributionPointCorrelationId = Guid.NewGuid();
		_eventReader = new(_bus, _distributionPointCorrelationId, null, 0, _abStreams, _ab12Tag, false, new RealTimeProvider());
		_eventReader.Resume();
		var correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Last(x => x.EventStreamId == "a").CorrelationId;
		_eventReader.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				correlationId, "a", 100, 100, ReadStreamResult.Success,
				[
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							1, 50, Guid.NewGuid(), Guid.NewGuid(), 50, 0, "a", ExpectedVersion.Any, DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd |
							PrepareFlags.IsJson,
							"event_type1", [1], [2])),
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							2, 150, Guid.NewGuid(), Guid.NewGuid(), 150, 0, "a", ExpectedVersion.Any,
							DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type2", [3], [4]))
				], null, false, "", 3, 2, true, 200));
		_eventReader.Handle(new ProjectionManagementMessage.Internal.ReadTimeout(correlationId, "a"));
		correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Last(x => x.EventStreamId == "b").CorrelationId;
		_eventReader.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				correlationId, "b", 100, 100, ReadStreamResult.Success,
				[
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							2, 100, Guid.NewGuid(), Guid.NewGuid(), 100, 0, "b", ExpectedVersion.Any,
							DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type1", [1], [2])),
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							3, 200, Guid.NewGuid(), Guid.NewGuid(), 200, 0, "b", ExpectedVersion.Any,
							DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type2", [3], [4]))
				], null, false, "", 4, 3, true, 200));
		_eventReader.Handle(new ProjectionManagementMessage.Internal.ReadTimeout(correlationId, "b"));
	}

	[Test]
	public void should_deliver_events() {
		Assert.AreEqual(3, _consumer.HandledMessages.OfType<ReaderSubscriptionMessage.CommittedEventDistributed>().Count());
	}
}
