// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Data;
using EventStore.Projections.Core.Messages;
using NUnit.Framework;
using EventStore.Core.Tests.Services.TimeService;

namespace EventStore.Projections.Core.Tests.Services.projection_subscription;

[TestFixture]
public class
	when_handling_enough_events_for_a_checkpoint_after_specified_time_elapses :
		TestFixtureWithProjectionSubscription {
	protected override void Given() {
		_checkpointAfterMs = 1000;
		_checkpointProcessedEventsThreshold = 1;
		_timeProvider = new FakeTimeProvider();
	}

	protected override void When() {
		_subscription.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				Guid.NewGuid(), new TFPos(200, 200), "test-stream", 1, false, Guid.NewGuid(),
				"bad-event-type", false, new byte[0], new byte[0]));
		_checkpointHandler.HandledMessages.Clear();
		((FakeTimeProvider)_timeProvider).AddToUtcTime(TimeSpan.FromMilliseconds(_checkpointAfterMs + 1));
		_subscription.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				Guid.NewGuid(), new TFPos(300, 300), "test-stream", 1, false, Guid.NewGuid(),
				"bad-event-type", false, new byte[0], new byte[0]));
	}

	[Test]
	public void checkpoint_is_suggested() {
		Assert.AreEqual(1, _checkpointHandler.HandledMessages.Count);
	}
}

[TestFixture]
public class
	when_handling_enough_events_for_a_checkpoint_after_specified_time_elapses_and_not_passing_filter :
		TestFixtureWithProjectionSubscription {
	protected override void Given() {
		_source = source => {
			source.FromAll();
			source.IncludeEvent("specific-event");
		};
		_checkpointAfterMs = 1000;
		_checkpointUnhandledBytesThreshold = 50;
		_checkpointProcessedEventsThreshold = 1;
		_timeProvider = new FakeTimeProvider();
	}

	protected override void When() {
		_subscription.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				Guid.NewGuid(), new TFPos(200, 200), "test-stream", 1, false, Guid.NewGuid(),
				"bad-event-type", false, new byte[0], new byte[0]));
		_subscription.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				Guid.NewGuid(), new TFPos(300, 300), "test-stream", 1, false, Guid.NewGuid(),
				"bad-event-type", false, new byte[0], new byte[0]));
		_checkpointHandler.HandledMessages.Clear();
		((FakeTimeProvider)_timeProvider).AddToUtcTime(TimeSpan.FromMilliseconds(_checkpointAfterMs + 1));
		_subscription.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				Guid.NewGuid(), new TFPos(400, 400), "test-stream", 1, false, Guid.NewGuid(),
				"bad-event-type", false, new byte[0], new byte[0]));
	}

	[Test]
	public void checkpoint_is_suggested() {
		Assert.AreEqual(1, _checkpointHandler.HandledMessages.Count);
	}
}
