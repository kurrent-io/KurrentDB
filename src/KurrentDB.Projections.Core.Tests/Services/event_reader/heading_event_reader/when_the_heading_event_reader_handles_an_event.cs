// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using KurrentDB.Core.Data;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Tests.Helpers;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.TransactionFile;
using NUnit.Framework;
using HeadingEventReader = KurrentDB.Projections.Core.Services.Processing.TransactionFile.HeadingEventReader;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader.heading_event_reader;

[TestFixture]
public class when_the_heading_event_reader_handles_an_event : TestFixtureWithReadWriteDispatchers {
	private HeadingEventReader _point;
	private Exception _exception;
	private Guid _distributionPointCorrelationId;

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
	}

	[Test]
	public void can_handle_next_event() {
		_point.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				_distributionPointCorrelationId, new TFPos(40, 30), "stream", 12, false, Guid.NewGuid(),
				"type", false, [], []));
	}

	[Test]
	public void cannot_handle_previous_event() {
		Assert.Throws<InvalidOperationException>(() => {
			_point.Handle(
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					_distributionPointCorrelationId, new TFPos(5, 0), "stream", 8, false, Guid.NewGuid(), "type",
					false, [], []));
		});
	}

	[Test]
	public void a_projection_can_be_subscribed_after_event_position() {
		var subscribed = _point.TrySubscribe(Guid.NewGuid(), new FakeReaderSubscription(), 30);
		Assert.AreEqual(true, subscribed);
	}

	[Test]
	public void a_projection_cannot_be_subscribed_at_earlier_position() {
		var subscribed = _point.TrySubscribe(Guid.NewGuid(), new FakeReaderSubscription(), 10);
		Assert.AreEqual(false, subscribed);
	}
}
