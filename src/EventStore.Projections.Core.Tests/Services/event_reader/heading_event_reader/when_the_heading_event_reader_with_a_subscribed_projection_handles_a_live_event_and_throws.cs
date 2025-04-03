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
using NUnit.Framework;
using HeadingEventReader = EventStore.Projections.Core.Services.Processing.TransactionFile.HeadingEventReader;

namespace EventStore.Projections.Core.Tests.Services.event_reader.heading_event_reader;

[TestFixture]
public class when_the_heading_event_reader_with_a_subscribed_projection_handles_a_live_event_and_throws :
	TestFixtureWithReadWriteDispatchers {
	private HeadingEventReader _point;
	private Guid _distibutionPointCorrelationId;
	private Guid _projectionSubscriptionId;

	[SetUp]
	public void setup() {
		_point = new HeadingEventReader(10, _bus);

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
		_projectionSubscriptionId = Guid.NewGuid();
		_point.TrySubscribe(_projectionSubscriptionId, new FakeReaderSubscription(), 30);
		_point.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				_distibutionPointCorrelationId, new TFPos(60, 50), "throws", 12, false, Guid.NewGuid(),
				"type", false, new byte[0], new byte[0]));
	}

	[Test]
	public void projection_is_notified_that_it_is_to_fault() {
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.Failed>().Count());
	}
}
