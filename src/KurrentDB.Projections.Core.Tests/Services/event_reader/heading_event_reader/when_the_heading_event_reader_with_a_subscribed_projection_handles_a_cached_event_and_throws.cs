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
public class when_the_heading_event_reader_with_a_subscribed_projection_handles_a_cached_event_and_throws :
	TestFixtureWithReadWriteDispatchers {
	private HeadingEventReader _point;
	private Guid _distributionPointCorrelationId;
	private Guid _projectionSubscriptionId;

	[SetUp]
	public void setup() {
		_point = new HeadingEventReader(10, _bus);

		_distributionPointCorrelationId = Guid.NewGuid();
		_point.Start(
			_distributionPointCorrelationId,
			new TransactionFileEventReader(_bus, _distributionPointCorrelationId, null, new TFPos(0, -1), new RealTimeProvider()));
		_point.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				_distributionPointCorrelationId, new TFPos(20, 10), "throws", 10, false, Guid.NewGuid(),
				"type", false, [], []));
		_point.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				_distributionPointCorrelationId, new TFPos(40, 30), "throws", 11, false, Guid.NewGuid(),
				"type", false, [], []));
		_projectionSubscriptionId = Guid.NewGuid();
		_point.TrySubscribe(_projectionSubscriptionId, new FakeReaderSubscription(), 30);
	}


	[Test]
	public void projection_is_notified_that_it_is_to_fault() {
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.Failed>().Count());
	}
}
