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
public class when_the_heading_event_reader_with_a_subscribed_projection_handles_an_idle_notification : TestFixtureWithReadWriteDispatchers {
	private HeadingEventReader _point;

	//private Exception _exception;
	private Guid _distributionPointCorrelationId;
	private FakeReaderSubscription _subscription;
	private Guid _projectionSubscriptionId;

	[SetUp]
	public void setup() {
		try {
			_point = new HeadingEventReader(10, _bus);
		} catch (Exception) {
			//
		}

		_distributionPointCorrelationId = Guid.NewGuid();
		_point.Start(
			_distributionPointCorrelationId,
			new TransactionFileEventReader(_bus, _distributionPointCorrelationId, null, new TFPos(0, -1), new RealTimeProvider()));
		DateTime timestamp = DateTime.UtcNow;
		_point.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				_distributionPointCorrelationId, new TFPos(20, 10), "stream", 10, false, Guid.NewGuid(),
				"type", false, [], [], timestamp));
		_point.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				_distributionPointCorrelationId, new TFPos(40, 30), "stream", 11, false, Guid.NewGuid(),
				"type", false, [], [], timestamp.AddMilliseconds(1)));
		_subscription = new FakeReaderSubscription();
		_projectionSubscriptionId = Guid.NewGuid();
		_point.TrySubscribe(_projectionSubscriptionId, _subscription, 30);
		_point.Handle(new ReaderSubscriptionMessage.EventReaderIdle(_distributionPointCorrelationId, timestamp.AddMilliseconds(1100)));
	}


	[Test]
	public void projection_receives_events_after_the_subscription_point() {
		Assert.AreEqual(1, _subscription.ReceivedIdleNotifications.Count());
	}
}
