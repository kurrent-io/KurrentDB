// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.Services.TimeService;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.SingleStream;
using KurrentDB.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;
using ReadStreamResult = KurrentDB.Core.Data.ReadStreamResult;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader.stream_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_onetime_reader_handles_eof<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private StreamEventReader _edp;

	//private Guid _publishWithCorrelationId;
	private Guid _distibutionPointCorrelationId;
	private Guid _firstEventId;
	private Guid _secondEventId;
	private FakeTimeProvider _fakeTimeProvider;

	protected override void Given() {
		TicksAreHandledImmediately();
	}

	[SetUp]
	public new void When() {
		//_publishWithCorrelationId = Guid.NewGuid();
		_distibutionPointCorrelationId = Guid.NewGuid();
		_fakeTimeProvider = new FakeTimeProvider();
		_edp = new StreamEventReader(_bus, _distibutionPointCorrelationId, null, "stream", 10, _fakeTimeProvider,
			resolveLinkTos: false, stopOnEof: true, produceStreamDeletes: false);
		_edp.Resume();
		_firstEventId = Guid.NewGuid();
		_secondEventId = Guid.NewGuid();
		var correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Last()
			.CorrelationId;
		_edp.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				correlationId, "stream", 100, 100, ReadStreamResult.Success,
				new[] {
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							10, 50, Guid.NewGuid(), _firstEventId, 50, 0, "stream", ExpectedVersion.Any,
							DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type1", new byte[] {1}, new byte[] {2})),
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							11, 100, Guid.NewGuid(), _secondEventId, 100, 0, "stream", ExpectedVersion.Any,
							DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type2", new byte[] {3}, new byte[] {4}))
				}, null, false, "", 12, 11, true, 200));
		correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Last()
			.CorrelationId;
		_edp.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				correlationId, "stream", 100, 100, ReadStreamResult.Success, new ResolvedEvent[] { }, null, false,
				"", 12, 11, true, 400));
	}

	[Test]
	public void publishes_eof_message() {
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ReaderSubscriptionMessage.EventReaderEof>().Count());
		var first = _consumer.HandledMessages.OfType<ReaderSubscriptionMessage.EventReaderEof>().First();
		Assert.AreEqual(first.CorrelationId, _distibutionPointCorrelationId);
	}

	[Test]
	public void does_not_publish_read_messages_anymore() {
		Assert.AreEqual(2, _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Count());
	}
}
