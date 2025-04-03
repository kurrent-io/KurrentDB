// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using EventStore.Core.Data;
using EventStore.Core.Messages;
using EventStore.Core.Services.AwakeReaderService;
using EventStore.Core.Services.TimerService;
using EventStore.Core.Tests;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Projections.Core.Messages;
using EventStore.Projections.Core.Services.Processing;
using EventStore.Projections.Core.Services.Processing.SingleStream;
using EventStore.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.event_reader.stream_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_handling_streams_with_deleted_events_and_reader_starting_at_event_zero<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private StreamEventReader _edp;
	private int _fromSequenceNumber;
	const string _streamName = "stream";

	protected override void Given() {
		TicksAreHandledImmediately();
		_fromSequenceNumber = 0;
	}

	[SetUp]
	public new void When() {
		_edp = new StreamEventReader(_bus, Guid.NewGuid(), null, _streamName, _fromSequenceNumber,
			new RealTimeProvider(), false,
			produceStreamDeletes: false);
		_edp.Resume();
	}

	private void HandleEvents(long[] eventNumbers) {
		string eventType = "event_type";
		List<ResolvedEvent> events = new List<ResolvedEvent>();

		foreach (long eventNumber in eventNumbers) {
			events.Add(
				ResolvedEvent.ForUnresolvedEvent(
					new EventRecord(
						eventNumber, 50 * (eventNumber + 1), Guid.NewGuid(), Guid.NewGuid(), 50 * (eventNumber + 1),
						0, _streamName, ExpectedVersion.Any, DateTime.UtcNow,
						PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
						eventType, new byte[] {0}, new byte[] {0}
					)
				)
			);
		}

		var correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Last()
			.CorrelationId;

		long start, end;
		if (eventNumbers.Length > 0) {
			start = eventNumbers[0];
			end = eventNumbers[eventNumbers.Length - 1];
		} else {
			start = _fromSequenceNumber;
			end = _fromSequenceNumber;
		}

		_edp.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				correlationId, _streamName, start, 100, ReadStreamResult.Success, events.ToArray(), null, false, "",
				start + 1, end, true, 200)
		);
	}

	private void HandleEvents(long start, long end) {
		List<long> eventNumbers = new List<long>();
		for (long i = start; i <= end; i++) eventNumbers.Add(i);
		HandleEvents(eventNumbers.ToArray());
	}

	[Test]
	public void allows_first_event_to_be_equal_to_sequence_number() {
		long eventSequenceNumber = _fromSequenceNumber;

		Assert.DoesNotThrow(() => { HandleEvents(eventSequenceNumber, eventSequenceNumber); });
	}

	[Test]
	public void allows_first_event_to_be_greater_than_sequence_number() {
		long eventSequenceNumber = _fromSequenceNumber + 5;

		Assert.DoesNotThrow(() => { HandleEvents(eventSequenceNumber, eventSequenceNumber); });
	}

	[Test]
	public void events_after_second_event_should_not_be_in_sequence() {
		//_fromSequenceNumber+2 has been omitted
		HandleEvents(new long[]
			{_fromSequenceNumber, _fromSequenceNumber + 1, _fromSequenceNumber + 3, _fromSequenceNumber + 4});

		Assert.AreEqual(2, HandledMessages.OfType<ReaderSubscriptionMessage.Faulted>().Count());
	}
}
