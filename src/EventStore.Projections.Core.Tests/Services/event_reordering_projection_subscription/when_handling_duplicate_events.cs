// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Projections.Core.Messages;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.event_reordering_projection_subscription;

[TestFixture]
public class when_handling_duplicate_events : TestFixtureWithEventReorderingProjectionSubscription {
	private DateTime _timestamp;

	protected override void When() {
		_timestamp = DateTime.UtcNow;
		_subscription.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				_projectionCorrelationId, new TFPos(200, 150), "a", 1, false, Guid.NewGuid(),
				"bad-event-type", false, new byte[0], new byte[0], _timestamp));
		_subscription.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				_projectionCorrelationId, new TFPos(100, 50), "a", 0, false, Guid.NewGuid(),
				"bad-event-type", false, new byte[0], new byte[0], _timestamp.AddMilliseconds(1)));
		_subscription.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				_projectionCorrelationId, new TFPos(200, 150), "a", 1, false, Guid.NewGuid(),
				"bad-event-type", false, new byte[0], new byte[0], _timestamp.AddMilliseconds(1)));
		_subscription.Handle(
			new ReaderSubscriptionMessage.EventReaderIdle(
				_projectionCorrelationId, _timestamp.AddMilliseconds(1100)));
	}

	[Test]
	public void duplicates_are_not_passed_to_downstream_handler() {
		Assert.AreEqual(2, _eventHandler.HandledMessages.Count);
	}
}
