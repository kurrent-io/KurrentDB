// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.TimerService;
using KurrentDB.Core.Tests;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Projections.Core.Messages;
using KurrentDB.Projections.Core.Services.Processing.MultiStream;
using KurrentDB.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader.multi_stream_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_handling_read_completed_for_all_streams_after_pause_requested<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private MultiStreamEventReader _edp;
	private Guid _distributionPointCorrelationId;
	private Guid _firstEventId;
	private Guid _secondEventId;
	private Guid _thirdEventId;
	private Guid _fourthEventId;

	protected override void Given() {
		TicksAreHandledImmediately();
	}

	private string[] _abStreams;
	private Dictionary<string, long> _ab12Tag;

	[SetUp]
	public new void When() {
		_ab12Tag = new() { { "a", 1 }, { "b", 2 } };
		_abStreams = ["a", "b"];

		_distributionPointCorrelationId = Guid.NewGuid();
		_edp = new(_bus, _distributionPointCorrelationId, null, 0, _abStreams, _ab12Tag, false, new RealTimeProvider());
		_edp.Resume();
		_firstEventId = Guid.NewGuid();
		_secondEventId = Guid.NewGuid();
		_thirdEventId = Guid.NewGuid();
		_fourthEventId = Guid.NewGuid();
		_edp.Pause();
		var correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Last(x => x.EventStreamId == "a").CorrelationId;
		_edp.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				correlationId, "a", 100, 100, ReadStreamResult.Success,
				[
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							1, 50, Guid.NewGuid(), _firstEventId, 50, 0, "a", ExpectedVersion.Any, DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type1", [1], [2])),
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							2, 150, Guid.NewGuid(), _secondEventId, 150, 0, "a", ExpectedVersion.Any, DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type2", [3], [4]))
				], null, false, "", 3, 2, true, 200));
		correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Last(x => x.EventStreamId == "b").CorrelationId;
		_edp.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				correlationId, "b", 100, 100, ReadStreamResult.Success,
				[
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							2, 100, Guid.NewGuid(), _thirdEventId, 100, 0, "b", ExpectedVersion.Any, DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type1", [1], [2])),
					ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							3, 200, Guid.NewGuid(), _fourthEventId, 200, 0, "b", ExpectedVersion.Any, DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type2", [3], [4]))
				], null, false, "", 4, 3, true, 200));
	}

	[Test]
	public void can_be_resumed() {
		_edp.Resume();
	}

	[Test]
	public void cannot_be_paused() {
		Assert.Throws<InvalidOperationException>(() => { _edp.Pause(); });
	}

	[Test]
	public void publishes_correct_number_of_committed_event_received_messages() {
		Assert.AreEqual(3, _consumer.HandledMessages.OfType<ReaderSubscriptionMessage.CommittedEventDistributed>().Count());
	}

	[Test]
	public void cannot_handle_following_read_events_completed() {
		var correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Last(x => x.EventStreamId == "a").CorrelationId;
		Assert.Throws<InvalidOperationException>(() => {
			_edp.Handle(
				new ClientMessage.ReadStreamEventsForwardCompleted(
					correlationId, "a", 100, 100, ReadStreamResult.Success,
					[
						ResolvedEvent.ForUnresolvedEvent(
							new EventRecord(
								3, 250, Guid.NewGuid(), Guid.NewGuid(), 250, 0, "a", ExpectedVersion.Any, DateTime.UtcNow,
								PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin |
								PrepareFlags.TransactionEnd,
								"event_type", [], []))
					], null, false, "", 4, 4, false, 300));
		});
	}
}
