// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.AwakeReaderService;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.SingleStream;
using EventStore.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Data.ReadStreamResult;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.event_reader.stream_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_handling_no_stream<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private StreamEventReader _edp;
	private Guid _distibutionPointCorrelationId;

	protected override void Given() {
		TicksAreHandledImmediately();
	}

	[SetUp]
	public new void When() {
		_distibutionPointCorrelationId = Guid.NewGuid();
		_edp = new StreamEventReader(_bus, _distibutionPointCorrelationId, null, "stream", 0,
			new RealTimeProvider(), false,
			produceStreamDeletes: false);
		_edp.Resume();
		var correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Last()
			.CorrelationId;
		_edp.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				correlationId, "stream", 100, 100,
				ReadStreamResult.NoStream, new ResolvedEvent[0], null, false, "", -1, ExpectedVersion.NoStream,
				true, 200));
	}

	[Test]
	public void cannot_be_resumed() {
		Assert.Throws<InvalidOperationException>(() => { _edp.Resume(); });
	}

	[Test]
	public void cannot_be_paused() {
		_edp.Pause();
	}

	[Test]
	public void publishes_read_events_from_beginning_with_correct_next_event_number() {
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Count());
		Assert.AreEqual(
			"stream",
			_consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Last().EventStreamId);
		Assert.AreEqual(
			0, _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Last().FromEventNumber);
	}

	[Test]
	public void publishes_correct_committed_event_received_messages() {
		Assert.AreEqual(
			1, _consumer.HandledMessages.OfType<ReaderSubscriptionMessage.CommittedEventDistributed>().Count());
		var first =
			_consumer.HandledMessages.OfType<ReaderSubscriptionMessage.CommittedEventDistributed>().Single();
		Assert.IsNull(first.Data);
		Assert.AreEqual(200, first.SafeTransactionFileReaderJoinPosition);
	}

	[Test]
	public void publishes_subscribe_awake() {
		Assert.AreEqual(2, _consumer.HandledMessages.OfType<AwakeServiceMessage.SubscribeAwake>().Count());
	}
}
