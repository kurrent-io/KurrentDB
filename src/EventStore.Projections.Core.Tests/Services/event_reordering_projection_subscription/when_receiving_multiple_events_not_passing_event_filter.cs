// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Projections.Core.Messages;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.event_reordering_projection_subscription;

[TestFixture]
public class when_receiving_multiple_events_not_passing_event_filter :
	TestFixtureWithEventReorderingProjectionSubscription {
	private DateTime _timestamp;

	protected override void Given() {
		base.Given();
		_source = builder => {
			builder.FromStream("a");
			builder.FromStream("b");
			builder.IncludeEvent("specific-event");
			builder.SetReorderEvents(true);
			builder.SetProcessingLag(1000); // ms
		};
	}

	protected override void When() {
		_timestamp = DateTime.UtcNow;
		_subscription.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				_projectionCorrelationId, new TFPos(200, 150), "a", 1, false, Guid.NewGuid(),
				"bad-event-type", false, new byte[0], new byte[0], _timestamp));
		_subscription.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				_projectionCorrelationId, new TFPos(2000, 950), "a", 2, false, Guid.NewGuid(),
				"bad-event-type", false, new byte[0], new byte[0], _timestamp.AddMilliseconds(1)));
		_subscription.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				_projectionCorrelationId, new TFPos(2100, 2050), "b", 1, false, Guid.NewGuid(),
				"bad-event-type", false, new byte[0], new byte[0], _timestamp.AddMilliseconds(1)));
		_subscription.Handle(
			new ReaderSubscriptionMessage.EventReaderIdle(
				_projectionCorrelationId, _timestamp.AddMilliseconds(1100)));
	}

	[Test]
	public void checkpoint_suggested_message_is_published_once_for_interval() {
		Assert.AreEqual(1, _checkpointHandler.HandledMessages.Count);
		Assert.AreEqual(2, _checkpointHandler.HandledMessages[0].CheckpointTag.Streams["a"]);
		Assert.AreEqual(1, _checkpointHandler.HandledMessages[0].CheckpointTag.Streams["b"]);
	}
}
