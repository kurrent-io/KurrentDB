// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests.Helpers;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.AllStream;
using NUnit.Framework;
using HeadingEventReader = EventStore.Projections.Core.Services.Processing.TransactionFile.HeadingEventReader;

namespace EventStore.Projections.Core.Tests.Services.event_reader.heading_event_reader;

[TestFixture]
public class when_starting_a_heading_event_reader : TestFixtureWithReadWriteDispatchers {
	private HeadingEventReader _point;
	private Exception _exception;
	private Guid _distibutionPointCorrelationId;

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
	}

	[Test]
	public void transaction_file_reader_publishes_read_events_from_tf() {
		Assert.IsTrue(_consumer.HandledMessages.OfType<ClientMessage.ReadAllEventsForward>().Any());
	}

	[Test]
	public void can_handle_events() {
		_point.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				_distibutionPointCorrelationId, new TFPos(20, 10), "stream", 10, false, Guid.NewGuid(),
				"type", false, new byte[0], new byte[0]));
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
