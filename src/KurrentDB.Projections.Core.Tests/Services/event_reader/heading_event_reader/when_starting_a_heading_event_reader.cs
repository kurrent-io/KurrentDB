// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Tests.Helpers;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.TransactionFile;
using NUnit.Framework;
using HeadingEventReader = KurrentDB.Projections.Core.Services.Processing.TransactionFile.HeadingEventReader;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader.heading_event_reader;

[TestFixture]
public class when_starting_a_heading_event_reader : TestFixtureWithReadWriteDispatchers {
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
	}

	[Test]
	public void transaction_file_reader_publishes_read_events_from_tf() {
		Assert.IsTrue(_consumer.HandledMessages.OfType<ClientMessage.ReadAllEventsForward>().Any());
	}

	[Test]
	public void can_handle_events() {
		_point.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				_distributionPointCorrelationId, new TFPos(20, 10), "stream", 10, false, Guid.NewGuid(),
				"type", false, [], []));
	}

	[Test]
	public void can_be_stopped() {
		_point.Stop();
	}


	[Test]
	public void cannot_suibscribe_even_from_reader_zero_position() {
		var subscribed = _point.TrySubscribe(Guid.NewGuid(), new FakeReaderSubscription(), -1);
		Assert.AreEqual(false, subscribed);
	}
}
