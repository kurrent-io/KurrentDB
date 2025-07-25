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
using KurrentDB.Projections.Core.Services.Processing.TransactionFile;
using KurrentDB.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader.transaction_file_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_onetime_reader_handles_eof<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private TransactionFileEventReader _edp;
	private Guid _distibutionPointCorrelationId;
	private Guid _firstEventId;
	private Guid _secondEventId;

	protected override void Given() {
		TicksAreHandledImmediately();
	}

	private FakeTimeProvider _fakeTimeProvider;

	[SetUp]
	public new void When() {
		_distibutionPointCorrelationId = Guid.NewGuid();
		_fakeTimeProvider = new FakeTimeProvider();
		_edp = new TransactionFileEventReader(_bus, _distibutionPointCorrelationId, null, new TFPos(100, 50),
			_fakeTimeProvider,
			deliverEndOfTFPosition: false, stopOnEof: true);
		_edp.Resume();
		_firstEventId = Guid.NewGuid();
		_secondEventId = Guid.NewGuid();
		var correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadAllEventsForward>().Last()
			.CorrelationId;
		_edp.Handle(
			new ClientMessage.ReadAllEventsForwardCompleted(
				correlationId, ReadAllResult.Success, null,
				new[] {
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							1, 50, Guid.NewGuid(), _firstEventId, 50, 0, "a", ExpectedVersion.Any,
							_fakeTimeProvider.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type1", new byte[] {1}, new byte[] {2}), 100),
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							2, 150, Guid.NewGuid(), _secondEventId, 150, 0, "b", ExpectedVersion.Any,
							_fakeTimeProvider.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type1", new byte[] {1}, new byte[] {2}), 200),
				}, null, false, 100, new TFPos(200, 150), new TFPos(500, -1), new TFPos(100, 50), 500));
		correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadAllEventsForward>().Last().CorrelationId;
		_edp.Handle(
			new ClientMessage.ReadAllEventsForwardCompleted(
				correlationId, ReadAllResult.Success, null,
				new ResolvedEvent[0], null, false, 100, new TFPos(), new TFPos(), new TFPos(),
				500));
	}

	[Test]
	public void publishes_eof_message() {
		Assert.AreEqual(1, _consumer.HandledMessages.OfType<ReaderSubscriptionMessage.EventReaderEof>().Count());
		var first = _consumer.HandledMessages.OfType<ReaderSubscriptionMessage.EventReaderEof>().First();
		Assert.AreEqual(first.CorrelationId, _distibutionPointCorrelationId);
	}

	[Test]
	public void does_not_publish_read_messages_anymore() {
		Assert.AreEqual(2, _consumer.HandledMessages.OfType<ClientMessage.ReadAllEventsForward>().Count());
	}
}
