// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Projections.Core.Messages;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.event_reordering_projection_subscription;

[TestFixture]
public class when_handling_an_idle_notification_and_reordering_is_required :
	TestFixtureWithEventReorderingProjectionSubscription {
	private Guid _firstEventId;
	private DateTime _firstEventTimestamp;
	private Guid _secondEventId;

	protected override void When() {
		_firstEventId = Guid.NewGuid();
		_secondEventId = Guid.NewGuid();
		_firstEventTimestamp = DateTime.UtcNow;

		_subscription.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				Guid.NewGuid(), new TFPos(200, 150), "a", 1, false, _secondEventId, "bad-event-type", false,
				new byte[0], new byte[0], _firstEventTimestamp));
		_subscription.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				Guid.NewGuid(), new TFPos(300, 100), "b", 1, false, _firstEventId, "bad-event-type", false,
				new byte[0], new byte[0], _firstEventTimestamp.AddMilliseconds(1)));
		_subscription.Handle(
			new ReaderSubscriptionMessage.EventReaderIdle(
				Guid.NewGuid(), _firstEventTimestamp.AddMilliseconds(_timeBetweenEvents)));
	}

	[Test]
	public void events_are_reordered() {
		Assert.AreEqual(2, _eventHandler.HandledMessages.Count);
		var first = _eventHandler.HandledMessages[0];
		var second = _eventHandler.HandledMessages[1];
		Assert.AreEqual(_firstEventId, first.Data.EventId);
		Assert.AreEqual(_secondEventId, second.Data.EventId);
	}
}
