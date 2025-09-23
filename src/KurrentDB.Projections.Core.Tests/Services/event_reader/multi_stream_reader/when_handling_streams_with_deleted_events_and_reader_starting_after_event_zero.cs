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
using ResolvedEvent = KurrentDB.Core.Data.ResolvedEvent;

namespace KurrentDB.Projections.Core.Tests.Services.event_reader.multi_stream_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_handling_streams_with_deleted_events_and_reader_starting_after_event_zero<TLogFormat, TStreamId>
	: TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private MultiStreamEventReader _edp;
	private int _fromSequenceNumber;
	private string[] _streamNames;
	private Dictionary<string, long> _streamPositions;
	private Guid _distributionPointCorrelationId;

	protected override void Given() {
		TicksAreHandledImmediately();
		_fromSequenceNumber = 10;
		_streamNames = ["stream1", "stream2"];
		_streamPositions = new() { { "stream1", _fromSequenceNumber }, { "stream2", 100 } };
	}

	[SetUp]
	public new void When() {
		_distributionPointCorrelationId = Guid.NewGuid();
		_edp = new(_bus, _distributionPointCorrelationId, null, 0, _streamNames, _streamPositions, false, new RealTimeProvider());
		_edp.Resume();
	}

	private void HandleEvents(string stream, long[] eventNumbers) {
		const string eventType = "event_type";
		List<ResolvedEvent> events = [];
		events.AddRange(
			eventNumbers.Select(eventNumber => ResolvedEvent.ForUnresolvedEvent(
				new EventRecord(eventNumber, 50 * (eventNumber + 1), Guid.NewGuid(),
					Guid.NewGuid(), 50 * (eventNumber + 1), 0, stream, ExpectedVersion.Any, DateTime.UtcNow,
					PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd, eventType, [0], [0]
				)
			)));
		long start, end;
		if (eventNumbers.Length > 0) {
			start = eventNumbers[0];
			end = eventNumbers[^1];
		} else {
			start = _fromSequenceNumber;
			end = _fromSequenceNumber;
		}

		var correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>().Last(x => x.EventStreamId == stream).CorrelationId;

		_edp.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				correlationId, stream, start, 100, ReadStreamResult.Success, events.ToArray(), null, false, "",
				start + 1, end, true, 200)
		);
	}

	private void HandleEvents(string stream, long start, long end) {
		List<long> eventNumbers = [];
		for (long i = start; i <= end; i++)
			eventNumbers.Add(i);
		HandleEvents(stream, eventNumbers.ToArray());
	}

	[Test]
	public void allows_first_event_to_be_equal_to_sequence_number() {
		long eventSequenceNumber = _fromSequenceNumber;

		Assert.DoesNotThrow(() => {
			HandleEvents(_streamNames[0], eventSequenceNumber, eventSequenceNumber);
			//to trigger event delivery:
			HandleEvents(_streamNames[1], 100, 101);
		});
	}

	[Test]
	public void should_not_allow_first_event_to_be_greater_than_sequence_number() {
		long eventSequenceNumber = _fromSequenceNumber + 5;

		HandleEvents(_streamNames[0], eventSequenceNumber, eventSequenceNumber);
		//to trigger event delivery:
		HandleEvents(_streamNames[1], 100, 101);

		Assert.AreEqual(1, HandledMessages.OfType<ReaderSubscriptionMessage.Faulted>().Count());
	}

	[Test]
	public void should_not_allow_first_event_to_be_less_than_sequence_number() {
		long eventSequenceNumber = _fromSequenceNumber - 1;

		HandleEvents(_streamNames[0], eventSequenceNumber, eventSequenceNumber);
		//to trigger event delivery:
		HandleEvents(_streamNames[1], 100, 101);

		Assert.AreEqual(1, HandledMessages.OfType<ReaderSubscriptionMessage.Faulted>().Count());
	}

	[Test]
	public void events_after_first_event_should_not_be_in_sequence() {
		//_fromSequenceNumber+2 has been omitted
		HandleEvents(_streamNames[0], [
			_fromSequenceNumber, _fromSequenceNumber + 1, _fromSequenceNumber + 3, _fromSequenceNumber + 4
		]);
		//to trigger event delivery:
		HandleEvents(_streamNames[1], 100, 101);

		Assert.AreEqual(2, HandledMessages.OfType<ReaderSubscriptionMessage.Faulted>().Count());
	}
}
