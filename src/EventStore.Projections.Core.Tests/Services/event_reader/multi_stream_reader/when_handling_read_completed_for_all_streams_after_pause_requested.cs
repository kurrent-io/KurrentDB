// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.MultiStream;
using EventStore.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;

namespace EventStore.Projections.Core.Tests.Services.event_reader.multi_stream_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_handling_read_completed_for_all_streams_after_pause_requested<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private MultiStreamEventReader _edp;
	private Guid _distibutionPointCorrelationId;
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
		_ab12Tag = new Dictionary<string, long> {{"a", 1}, {"b", 2}};
		_abStreams = new[] {"a", "b"};

		_distibutionPointCorrelationId = Guid.NewGuid();
		_edp = new MultiStreamEventReader(
			_ioDispatcher, _bus, _distibutionPointCorrelationId, null, 0, _abStreams, _ab12Tag, false,
			new RealTimeProvider());
		_edp.Resume();
		_firstEventId = Guid.NewGuid();
		_secondEventId = Guid.NewGuid();
		_thirdEventId = Guid.NewGuid();
		_fourthEventId = Guid.NewGuid();
		_edp.Pause();
		var correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>()
			.Last(x => x.EventStreamId == "a").CorrelationId;
		_edp.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				correlationId, "a", 100, 100, ReadStreamResult.Success,
				new[] {
					EventStore.Core.Data.ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							1, 50, Guid.NewGuid(), _firstEventId, 50, 0, "a", ExpectedVersion.Any, DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type1", new byte[] {1}, new byte[] {2})),
					EventStore.Core.Data.ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							2, 150, Guid.NewGuid(), _secondEventId, 150, 0, "a", ExpectedVersion.Any,
							DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type2", new byte[] {3}, new byte[] {4}))
				}, null, false, "", 3, 2, true, 200));
		correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>()
			.Last(x => x.EventStreamId == "b").CorrelationId;
		_edp.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				correlationId, "b", 100, 100, ReadStreamResult.Success,
				new[] {
					EventStore.Core.Data.ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							2, 100, Guid.NewGuid(), _thirdEventId, 100, 0, "b", ExpectedVersion.Any,
							DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type1", new byte[] {1}, new byte[] {2})),
					EventStore.Core.Data.ResolvedEvent.ForUnresolvedEvent(
						new EventRecord(
							3, 200, Guid.NewGuid(), _fourthEventId, 200, 0, "b", ExpectedVersion.Any,
							DateTime.UtcNow,
							PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
							"event_type2", new byte[] {3}, new byte[] {4}))
				}, null, false, "", 4, 3, true, 200));
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
		Assert.AreEqual(
			3, _consumer.HandledMessages.OfType<ReaderSubscriptionMessage.CommittedEventDistributed>().Count());
	}

	[Test]
	public void cannot_handle_following_read_events_completed() {
		var correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>()
			.Last(x => x.EventStreamId == "a").CorrelationId;
		Assert.Throws<InvalidOperationException>(() => {
			_edp.Handle(
				new ClientMessage.ReadStreamEventsForwardCompleted(
					correlationId, "a", 100, 100, ReadStreamResult.Success,
					new[] {
						EventStore.Core.Data.ResolvedEvent.ForUnresolvedEvent(
							new EventRecord(
								3, 250, Guid.NewGuid(), Guid.NewGuid(), 250, 0, "a", ExpectedVersion.Any,
								DateTime.UtcNow,
								PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin |
								PrepareFlags.TransactionEnd,
								"event_type", new byte[0], new byte[0]))
					}, null, false, "", 4, 4, false, 300));
		});
	}
}
