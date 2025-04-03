// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using System.Collections.Generic;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Tests;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.Tests.Services.TimeService;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.EventByType;
using NUnit.Framework;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.event_reader.event_by_type_index_event_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_tf_based_read_timeout_occurs<TLogFormat, TStreamId> : EventByTypeIndexEventReaderTestFixture<TLogFormat, TStreamId> {
	private EventByTypeIndexEventReader _eventReader;
	private Guid _distributionCorrelationId;
	private Guid _readAllEventsForwardCorrelationId;

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
			false, new TFPos(0, 0),
			fromPositions, true,
			_fakeTimeProvider,
			stopOnEof: true);

		_eventReader.Resume();

		CompleteForwardStreamRead("$et-eventTypeOne", Guid.Empty);
		CompleteForwardStreamRead("$et-eventTypeTwo", Guid.Empty);
		CompleteBackwardStreamRead("$et", Guid.Empty);

		_readAllEventsForwardCorrelationId = TimeoutRead("$all", Guid.Empty);

		CompleteForwardAllStreamRead(_readAllEventsForwardCorrelationId, new[] {
			ResolvedEvent.ForUnresolvedEvent(
				new EventRecord(
					1, 50, Guid.NewGuid(), Guid.NewGuid(), 50, 0, "test_stream", ExpectedVersion.Any,
					_fakeTimeProvider.UtcNow,
					PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
					"eventTypeOne", new byte[] {1}, new byte[] {2}), 100),
			ResolvedEvent.ForUnresolvedEvent(
				new EventRecord(
					2, 150, Guid.NewGuid(), Guid.NewGuid(), 150, 0, "test_stream", ExpectedVersion.Any,
					_fakeTimeProvider.UtcNow,
					PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
					"eventTypeTwo", new byte[] {1}, new byte[] {2}), 200),
		});
	}

	[Test]
	public void should_not_deliver_events() {
		Assert.AreEqual(0,
			_consumer.HandledMessages.OfType<ReaderSubscriptionMessage.CommittedEventDistributed>().Count());
	}

	[Test]
	public void should_attempt_another_read_for_the_timed_out_reads() {
		var readAllEventsForwardMessages = _consumer.HandledMessages.OfType<ClientMessage.ReadAllEventsForward>();

		Assert.AreEqual(readAllEventsForwardMessages.First().CorrelationId, _readAllEventsForwardCorrelationId);
		Assert.AreEqual(1, readAllEventsForwardMessages.Skip(1).Count());
	}
}
