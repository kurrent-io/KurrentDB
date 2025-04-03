// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.SingleStream;
using EventStore.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;
using ReadStreamResult = EventStore.Core.Data.ReadStreamResult;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.event_reader.stream_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_resuming_stream_event_reader<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private StreamEventReader _edp;
	private Guid _distibutionPointCorrelationId;

	[SetUp]
	public new void When() {
		_distibutionPointCorrelationId = Guid.NewGuid();
		_edp = new StreamEventReader(_bus, _distibutionPointCorrelationId, null, "stream", 10,
			new RealTimeProvider(), false,
			produceStreamDeletes: false);
		_edp.Resume();
	}

	[Test]
	public void it_cannot_be_resumed() {
		Assert.Throws<InvalidOperationException>(() => { _edp.Resume(); });
	}

	[Test]
	public void it_cannot_be_paused() {
		_edp.Pause();
	}

	[Test]
	public void it_publishes_read_events_from_beginning() {
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Count());
		Assert.AreEqual(
			"stream",
			_consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Single().EventStreamId);
		Assert.AreEqual(
			10, _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Single().FromEventNumber);
	}

	[Test]
	public void can_handle_read_events_completed() {
		_edp.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				_distibutionPointCorrelationId, "stream", 100, 100, ReadStreamResult.Success,
				new[] {
					ResolvedEvent.ForUnresolvedEvent(new EventRecord(
						10, 50, Guid.NewGuid(), Guid.NewGuid(), 50, 0, "stream", ExpectedVersion.Any,
						DateTime.UtcNow,
						PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
						"event_type", new byte[0], new byte[0]), 0)
				}, null, false, "", 11, 10, true, 100));
	}
}
