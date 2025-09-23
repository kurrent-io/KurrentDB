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
public class when_heading_event_reader_has_been_created : TestFixtureWithReadWriteDispatchers {
	private HeadingEventReader _point;
	private Exception _exception;

	[SetUp]
	public void setup() {
		_exception = null;
		try {
			_point = new HeadingEventReader(10, _bus);
		} catch (Exception ex) {
			_exception = ex;
		}
	}

	[Test]
	public void it_has_been_created() {
		Assert.IsNull(_exception, ((object)_exception ?? "").ToString());
	}

	[Test]
	public void stop_throws_invalid_operation_exception() {
		Assert.Throws<InvalidOperationException>(() => { _point.Stop(); });
	}

	[Test]
	public void try_subscribe_throws_invalid_operation_exception() {
		Assert.Throws<InvalidOperationException>(() => {
			_point.TrySubscribe(Guid.NewGuid(), new FakeReaderSubscription(), 10);
		});
	}

	[Test]
	public void usubscribe_throws_invalid_operation_exception() {
		Assert.Throws<InvalidOperationException>(() => { _point.Unsubscribe(Guid.NewGuid()); });
	}

	[Test]
	public void handle_throws_invalid_operation_exception() {
		Assert.Throws<InvalidOperationException>(() => {
			_point.Handle(
				ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
					Guid.NewGuid(), new TFPos(20, 10), "stream", 10, false, Guid.NewGuid(), "type", false,
					[], []));
		});
	}

	[Test]
	public void can_be_started() {
		var eventReaderId = Guid.NewGuid();
		_point.Start(eventReaderId, new TransactionFileEventReader(_bus, eventReaderId, null, new TFPos(0, -1), new RealTimeProvider()));
	}
}
