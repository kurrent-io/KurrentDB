// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Linq;
using EventStore.Core.Messages;
using EventStore.Core.Tests;
using EventStore.Core.Tests.Services.TimeService;
using KurrentDB.Core.Data;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.TransactionFile;
using KurrentDB.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader.transaction_file_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_read_completes_before_timeout<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private TransactionFileEventReader _eventReader;
	private Guid _distributionCorrelationId;

	protected override void Given() {
		TicksAreHandledImmediately();
	}

	private FakeTimeProvider _fakeTimeProvider;

	[SetUp]
	public new void When() {
		_distributionCorrelationId = Guid.NewGuid();
		_fakeTimeProvider = new FakeTimeProvider();
		_eventReader = new TransactionFileEventReader(_bus, _distributionCorrelationId, null, new TFPos(100, 50),
			_fakeTimeProvider,
			deliverEndOfTFPosition: false, stopOnEof: true);
		_eventReader.Resume();
		var correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadAllEventsForward>().Last()
			.CorrelationId;
		_eventReader.Handle(
			new ClientMessage.ReadAllEventsForwardCompleted(
				correlationId, ReadAllResult.Success, null,
				new[] {
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							1, 50, Guid.NewGuid(), Guid.NewGuid(), 50, 0, "a", ExpectedVersion.Any,
							_fakeTimeProvider.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type1", new byte[] {1}, new byte[] {2}), 100),
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							2, 150, Guid.NewGuid(), Guid.NewGuid(), 150, 0, "b", ExpectedVersion.Any,
							_fakeTimeProvider.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type1", new byte[] {1}, new byte[] {2}), 200),
				}, null, false, 100, new TFPos(200, 150), new TFPos(500, -1), new TFPos(100, 50), 500));
		_eventReader.Handle(
			new ProjectionManagementMessage.Internal.ReadTimeout(correlationId, "$all"));
	}

	[Test]
	public void should_deliver_events() {
		Assert.AreEqual(2,
			_consumer.HandledMessages.OfType<ReaderSubscriptionMessage.CommittedEventDistributed>().Count());
	}
}
