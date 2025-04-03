// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.Tests.Services.TimeService;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Tests.Services.core_projection;
using EventStore.Common.Utils;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Services.Processing.EventByType;
using NUnit.Framework;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.event_reader.event_by_type_index_event_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_index_based_read_completes_before_timeout<TLogFormat, TStreamId> : EventByTypeIndexEventReaderTestFixture<TLogFormat, TStreamId> {
	private EventByTypeIndexEventReader _eventReader;
	private Guid _distributionCorrelationId;

	protected override void Given() {
		TicksAreHandledImmediately();
	}

	private FakeTimeProvider _fakeTimeProvider;

	[SetUp]
	public new void When() {
		_distributionCorrelationId = Guid.NewGuid();
		_fakeTimeProvider = new FakeTimeProvider();
		var fromPositions = new Dictionary<string, long>();
		fromPositions.Add("$et-eventTypeOne", 0);
		fromPositions.Add("$et-eventTypeTwo", 0);
		_eventReader = new EventByTypeIndexEventReader(_bus, _distributionCorrelationId,
			null, new string[] {"eventTypeOne", "eventTypeTwo"},
			false, new TFPos(-1, -1),
			fromPositions, true,
			_fakeTimeProvider,
			stopOnEof: true);

		_eventReader.Resume();

		var correlationId = CompleteForwardStreamRead("$et-eventTypeOne", Guid.Empty, new[] {
			ResolvedEvent.ForUnresolvedEvent(
				new EventRecord(
					1, 50, Guid.NewGuid(), Guid.NewGuid(), 50, 0, "$et-eventTypeOne", ExpectedVersion.Any,
					DateTime.UtcNow,
					PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd |
					PrepareFlags.IsJson,
					"$>", Helper.UTF8NoBom.GetBytes("0@test-stream"),
					Helper.UTF8NoBom.GetBytes(TFPosToMetadata(new TFPos(50, 50))))),
			ResolvedEvent.ForUnresolvedEvent(
				new EventRecord(
					2, 150, Guid.NewGuid(), Guid.NewGuid(), 150, 0, "$et-eventTypeOne", ExpectedVersion.Any,
					DateTime.UtcNow,
					PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
					"$>", Helper.UTF8NoBom.GetBytes("1@test-stream"),
					Helper.UTF8NoBom.GetBytes(TFPosToMetadata(new TFPos(150, 150)))))
		});

		TimeoutRead("$et-eventTypeOne", correlationId);

		correlationId = CompleteForwardStreamRead("$et-eventTypeTwo", Guid.Empty, new[] {
			ResolvedEvent.ForUnresolvedEvent(
				new EventRecord(
					1, 100, Guid.NewGuid(), Guid.NewGuid(), 100, 0, "$et-eventTypeTwo", ExpectedVersion.Any,
					DateTime.UtcNow,
					PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd |
					PrepareFlags.IsJson,
					"$>", Helper.UTF8NoBom.GetBytes("2@test-stream"),
					Helper.UTF8NoBom.GetBytes(TFPosToMetadata(new TFPos(100, 100))))),
			ResolvedEvent.ForUnresolvedEvent(
				new EventRecord(
					2, 200, Guid.NewGuid(), Guid.NewGuid(), 200, 0, "$et-eventTypeTwo", ExpectedVersion.Any,
					DateTime.UtcNow,
					PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
					"$>", Helper.UTF8NoBom.GetBytes("3@test-stream"),
					Helper.UTF8NoBom.GetBytes(TFPosToMetadata(new TFPos(200, 200)))))
		});

		TimeoutRead("$et-eventTypeTwo", correlationId);
	}

	[Test]
	public void should_deliver_events() {
		Assert.AreEqual(3,
			_consumer.HandledMessages.OfType<ReaderSubscriptionMessage.CommittedEventDistributed>().Count());
	}
}
