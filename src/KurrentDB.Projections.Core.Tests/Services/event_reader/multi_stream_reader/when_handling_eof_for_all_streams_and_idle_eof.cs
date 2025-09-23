// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Tests;
using KurrentDB.Core.Tests.Services.TimeService;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.MultiStream;
using KurrentDB.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;
using AwakeServiceMessage = KurrentDB.Core.Services.AwakeReaderService.AwakeServiceMessage;
using ReadStreamResult = KurrentDB.Core.Data.ReadStreamResult;
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader.multi_stream_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_handling_eof_for_all_streams_and_idle_eof<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private MultiStreamEventReader _edp;
	private Guid _distributionPointCorrelationId;
	private Guid _firstEventId;
	private Guid _secondEventId;

	protected override void Given() {
		TicksAreHandledImmediately();
	}

	private string[] _abStreams;
	private Dictionary<string, long> _ab12Tag;
	private FakeTimeProvider _fakeTimeProvider;

	[SetUp]
	public new void When() {
		_ab12Tag = new() { { "a", 1 }, { "b", 2 } };
		_abStreams = ["a", "b"];

		_distributionPointCorrelationId = Guid.NewGuid();
		_fakeTimeProvider = new FakeTimeProvider();
		_edp = new(_bus, _distributionPointCorrelationId, null, 0, _abStreams, _ab12Tag, false, _fakeTimeProvider);
		_edp.Resume();
		_firstEventId = Guid.NewGuid();
		_secondEventId = Guid.NewGuid();
		var correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>()
			.Last(x => x.EventStreamId == "a").CorrelationId;
		_edp.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				correlationId, "a", 100, 100, ReadStreamResult.Success,
				[
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							1, 50, Guid.NewGuid(), _firstEventId, 50, 0, "a", ExpectedVersion.Any,
							_fakeTimeProvider.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type1", [1], [2]))
				], null, false, "", 2, 1, true, 200));
		correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Last(x => x.EventStreamId == "b").CorrelationId;
		_edp.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				correlationId, "b", 100, 100, ReadStreamResult.Success,
				[
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							2, 100, Guid.NewGuid(), _secondEventId, 100, 0, "b", ExpectedVersion.Any,
							_fakeTimeProvider.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type1", [1], [2]))
				], null, false, "", 3, 2, true, 200));
		correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Last(x => x.EventStreamId == "a").CorrelationId;
		_edp.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(correlationId, "a", 100, 100, ReadStreamResult.Success, [], null, false, "", 2, 1, true, 400));
		correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Last(x => x.EventStreamId == "b").CorrelationId;
		_edp.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(correlationId, "b", 100, 100, ReadStreamResult.Success, [], null, false, "", 3, 2, true, 400));
		_fakeTimeProvider.AddToUtcTime(TimeSpan.FromMilliseconds(500));
		correlationId =
			((ClientMessage.ReadStreamEventsForward)(_consumer.HandledMessages.OfType<AwakeServiceMessage.SubscribeAwake>().Last().ReplyWithMessage))
			.CorrelationId;
		_edp.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(correlationId, "a", 100, 100, ReadStreamResult.Success, [], null, false, "", 2, 1, true, 600));
	}

	[Test]
	public void publishes_event_distribution_idle_messages() {
		Assert.AreEqual(2, _consumer.HandledMessages.OfType<ReaderSubscriptionMessage.EventReaderIdle>().Count());
		var first = _consumer.HandledMessages.OfType<ReaderSubscriptionMessage.EventReaderIdle>().First();
		var second = _consumer.HandledMessages.OfType<ReaderSubscriptionMessage.EventReaderIdle>().Skip(1).First();

		Assert.AreEqual(first.CorrelationId, _distributionPointCorrelationId);
		Assert.AreEqual(second.CorrelationId, _distributionPointCorrelationId);

		Assert.AreEqual(TimeSpan.FromMilliseconds(500), second.IdleTimestampUtc - first.IdleTimestampUtc);
	}
}
