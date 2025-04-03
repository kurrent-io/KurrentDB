// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.Storage.ReaderIndex;
using EventStore.Core.Tests.Services.TimeService;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;
using EventStore.Core.Services.AwakeReaderService;
using EventStore.Core.Tests;
using EventStore.Projections.Core.Services.Processing.AllStream;

namespace EventStore.Projections.Core.Tests.Services.event_reader.transaction_file_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_handling_eof_and_idle_eof<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
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
			deliverEndOfTFPosition: false);
		_edp.Resume();
		_firstEventId = Guid.NewGuid();
		_secondEventId = Guid.NewGuid();
		var correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadAllEventsForward>().Last()
			.CorrelationId;
		_edp.Handle(
			new ClientMessage.ReadAllEventsForwardCompleted(
				correlationId, ReadAllResult.Success, null,
				new[] {
					EventStore.Core.Data.ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							1, 50, Guid.NewGuid(), _firstEventId, 50, 0, "a", ExpectedVersion.Any,
							_fakeTimeProvider.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type1", new byte[] {1}, new byte[] {2}), 100),
					EventStore.Core.Data.ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							2, 150, Guid.NewGuid(), _secondEventId, 150, 0, "b", ExpectedVersion.Any,
							_fakeTimeProvider.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type1", new byte[] {1}, new byte[] {2}), 200),
				}, null, false, 100,
				new TFPos(200, 150), new TFPos(500, -1), new TFPos(100, 50), 500));
		correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadAllEventsForward>().Last().CorrelationId;
		_edp.Handle(
			new ClientMessage.ReadAllEventsForwardCompleted(
				correlationId, ReadAllResult.Success, null,
				new EventStore.Core.Data.ResolvedEvent[0], null, false, 100, new TFPos(), new TFPos(), new TFPos(),
				500));
		_fakeTimeProvider.AddToUtcTime(TimeSpan.FromMilliseconds(500));
		correlationId = ((ClientMessage.ReadAllEventsForward)(_consumer.HandledMessages
			.OfType<AwakeServiceMessage.SubscribeAwake>().Last().ReplyWithMessage)).CorrelationId;
		_edp.Handle(
			new ClientMessage.ReadAllEventsForwardCompleted(
				correlationId, ReadAllResult.Success, null,
				new EventStore.Core.Data.ResolvedEvent[0], null, false, 100, new TFPos(), new TFPos(), new TFPos(),
				500));
	}

	[Test]
	public void publishes_event_distribution_idle_messages() {
		Assert.AreEqual(
			2, _consumer.HandledMessages.OfType<ReaderSubscriptionMessage.EventReaderIdle>().Count());
		var first =
			_consumer.HandledMessages.OfType<ReaderSubscriptionMessage.EventReaderIdle>().First();
		var second =
			_consumer.HandledMessages.OfType<ReaderSubscriptionMessage.EventReaderIdle>()
				.Skip(1)
				.First();

		Assert.AreEqual(first.CorrelationId, _distibutionPointCorrelationId);
		Assert.AreEqual(second.CorrelationId, _distibutionPointCorrelationId);

		Assert.AreEqual(TimeSpan.FromMilliseconds(500), second.IdleTimestampUtc - first.IdleTimestampUtc);
	}
}
