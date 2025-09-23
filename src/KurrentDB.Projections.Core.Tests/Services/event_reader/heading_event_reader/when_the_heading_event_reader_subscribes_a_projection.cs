// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Tests.Helpers;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.TransactionFile;
using NUnit.Framework;
using HeadingEventReader = KurrentDB.Projections.Core.Services.Processing.TransactionFile.HeadingEventReader;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader.heading_event_reader;

[TestFixture]
public class when_the_heading_event_reader_subscribes_a_projection : TestFixtureWithReadWriteDispatchers {
	private HeadingEventReader _point;
	private Exception _exception;
	private Guid _distributionPointCorrelationId;
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

		_distributionPointCorrelationId = Guid.NewGuid();
		_point.Start(
			_distributionPointCorrelationId,
			new TransactionFileEventReader(_bus, _distributionPointCorrelationId, null, new TFPos(0, -1), new RealTimeProvider()));
		_point.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				_distributionPointCorrelationId, new TFPos(20, 10), "stream", 10, false, Guid.NewGuid(),
				"type", false, [], []));
		_point.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				_distributionPointCorrelationId, new TFPos(40, 30), "stream", 11, false, Guid.NewGuid(),
				"type", false, [], []));
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
