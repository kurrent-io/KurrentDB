// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests.Helpers;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.AllStream;
using EventStore.Projections.Core.Tests.Services.projections_manager.managed_projection;
using NUnit.Framework;
using HeadingEventReader = EventStore.Projections.Core.Services.Processing.TransactionFile.HeadingEventReader;

namespace EventStore.Projections.Core.Tests.Services.event_reader.heading_event_reader;

[TestFixture]
public class when_the_heading_event_reader_subscribes_a_projection : TestFixtureWithReadWriteDispatchers {
	private HeadingEventReader _point;
	private Exception _exception;
	private Guid _distibutionPointCorrelationId;
	private FakeReaderSubscription _subscription;
	private Guid _projectionSubscriptionId;

	[SetUp]
	public void setup() {
		_exception = null;
		try {
			_point = new HeadingEventReader(10, _bus);
		} catch (Exception ex) {
			_exception = ex;
		}

		Assume.That(_exception == null);

		_distibutionPointCorrelationId = Guid.NewGuid();
		_point.Start(
			_distibutionPointCorrelationId,
			new TransactionFileEventReader(_bus, _distibutionPointCorrelationId, null, new TFPos(0, -1),
				new RealTimeProvider()));
		_point.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				_distibutionPointCorrelationId, new TFPos(20, 10), "stream", 10, false, Guid.NewGuid(),
				"type", false, new byte[0], new byte[0]));
		_point.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				_distibutionPointCorrelationId, new TFPos(40, 30), "stream", 11, false, Guid.NewGuid(),
				"type", false, new byte[0], new byte[0]));
		_subscription = new FakeReaderSubscription();
		_projectionSubscriptionId = Guid.NewGuid();
		_point.TrySubscribe(_projectionSubscriptionId, _subscription, 30);
	}


	[Test]
	public void projection_receives_at_least_one_cached_event_before_the_subscription_position() {
		Assert.AreEqual(true, _subscription.ReceivedEvents.Any(v => v.Data.Position.PreparePosition <= 30));
	}

	[Test]
	public void projection_receives_all_the_previously_handled_events_after_the_subscription_position() {
		Assert.AreEqual(true, _subscription.ReceivedEvents.Any(v => v.Data.Position.PreparePosition == 30));
	}

	[Test]
	public void it_can_be_unsubscribed() {
		_point.Unsubscribe(_projectionSubscriptionId);
	}

	[Test]
	public void no_other_projection_can_subscribe_with_the_same_projection_id() {
		Assert.Throws<InvalidOperationException>(() => {
			_point.TrySubscribe(_projectionSubscriptionId, _subscription, 30);
		});
	}
}
