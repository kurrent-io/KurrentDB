// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Data;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.Checkpointing;
using KurrentDB.Projections.Core.Services.Processing.Subscriptions;
using KurrentDB.Projections.Core.Tests.Services.event_reader.heading_event_reader;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.core_service;

[TestFixture]
public class when_a_subscribed_projection_handler_throws : TestFixtureWithProjectionCoreService {
	[SetUp]
	public new void Setup() {
		var readerStrategy = new FakeReaderStrategy();
		var projectionCorrelationId = Guid.NewGuid();
		_readerService.Handle(
			new ReaderSubscriptionManagement.Subscribe(
				projectionCorrelationId, CheckpointTag.FromPosition(0, 0, 0), readerStrategy,
				new ReaderSubscriptionOptions(1000, 2000, 10000, false, stopAfterNEvents: null, true)));
		_readerService.Handle(
			ReaderSubscriptionMessage.CommittedEventDistributed.Sample(
				readerStrategy.EventReaderId, new TFPos(20, 10), "throws", 10, false, Guid.NewGuid(),
				"type", false, new byte[0], new byte[0]));
	}

	[Test]
	public void projection_is_notified_that_it_is_to_fault() {
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<EventReaderSubscriptionMessage.Failed>().Count());
	}
}
