// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Projections.Core.Messages;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.projection_subscription;

[TestFixture]
public class when_receiving_multiple_events_not_passing_event_filter : TestFixtureWithProjectionSubscription {
	protected override void Given() {
		_source = source => {
			source.FromAll();
			source.IncludeEvent("specific-event");
		};
	}

	protected override void When() {
		_subscription.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				Guid.NewGuid(), new TFPos(200, 150), "test-stream", 1, false, Guid.NewGuid(),
				"bad-event-type", false, new byte[0], new byte[0]));
		_subscription.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				Guid.NewGuid(), new TFPos(2000, 1950), "test-stream", 2, false, Guid.NewGuid(),
				"bad-event-type", false, new byte[0], new byte[0]));
		_subscription.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				Guid.NewGuid(), new TFPos(2100, 2050), "test-stream", 2, false, Guid.NewGuid(),
				"bad-event-type", false, new byte[0], new byte[0]));
	}

	[Test]
	public void checkpoint_suggested_message_is_published_once_for_interval() {
		Assert.AreEqual(1, _checkpointHandler.HandledMessages.Count);
		Assert.AreEqual(2000, _checkpointHandler.HandledMessages[0].CheckpointTag.CommitPosition);
		Assert.AreEqual(1950, _checkpointHandler.HandledMessages[0].CheckpointTag.PreparePosition);
	}
}
