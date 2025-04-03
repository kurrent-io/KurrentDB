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
using EventStore.Projections.Core.Services.Processing.MultiStream;
using EventStore.Projections.Core.Tests.Services.core_projection;
using NUnit.Framework;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.event_reader.multi_stream_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class
	when_handling_streams_with_deleted_events_and_reader_starting_at_event_zero<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private MultiStreamEventReader _edp;
	private int _fromSequenceNumber;
	private string[] _streamNames;
	private Dictionary<string, long> _streamPositions;
	private Guid _distibutionPointCorrelationId;

	protected override void Given() {
		TicksAreHandledImmediately();
		_fromSequenceNumber = 0;
		_streamNames = new[] {"stream1", "stream2"};
		_streamPositions = new Dictionary<string, long> {{"stream1", _fromSequenceNumber}, {"stream2", 100}};
	}

	[SetUp]
	public new void When() {
		_distibutionPointCorrelationId = Guid.NewGuid();

		_edp = new MultiStreamEventReader(
			_ioDispatcher, _bus, _distibutionPointCorrelationId, null, 0, _streamNames, _streamPositions, false,
			new RealTimeProvider());

		_edp.Resume();
	}

	private void HandleEvents(string stream, long[] eventNumbers) {
		string eventType = "event_type";
		List<ResolvedEvent> events = new List<ResolvedEvent>();

		foreach (long eventNumber in eventNumbers) {
			events.Add(
				ResolvedEvent.ForUnresolvedEvent(
					new EventRecord(
						eventNumber, 50 * (eventNumber + 1), Guid.NewGuid(), Guid.NewGuid(), 50 * (eventNumber + 1),
						0, stream, ExpectedVersion.Any, DateTime.UtcNow,
						PrepareFlags.SingleWrite | PrepareFlags.TransactionBegin | PrepareFlags.TransactionEnd,
						eventType, new byte[] {0}, new byte[] {0}
					)
				)
			);
		}

		long start, end;
		if (eventNumbers.Length > 0) {
			start = eventNumbers[0];
			end = eventNumbers[eventNumbers.Length - 1];
		} else {
			start = _fromSequenceNumber;
			end = _fromSequenceNumber;
		}

		var correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>()
			.Last(x => x.EventStreamId == stream).CorrelationId;

		_edp.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				correlationId, stream, start, 100, ReadStreamResult.Success, events.ToArray(), null, false, "",
				start + 1, end, true, 200)
		);
	}

	private void HandleEvents(string stream, long start, long end) {
		List<long> eventNumbers = new List<long>();
		for (long i = start; i <= end; i++) eventNumbers.Add(i);
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
	public void allows_first_event_to_be_greater_than_sequence_number() {
		long eventSequenceNumber = _fromSequenceNumber + 5;

		Assert.DoesNotThrow(() => {
			HandleEvents(_streamNames[0], eventSequenceNumber, eventSequenceNumber);
			//to trigger event delivery:
			HandleEvents(_streamNames[1], 100, 101);
		});
	}

	[Test]
	public void events_after_first_event_should_not_be_in_sequence() {
		//_fromSequenceNumber+2 has been omitted
		HandleEvents(_streamNames[0],
			new long[] {
				_fromSequenceNumber, _fromSequenceNumber + 1, _fromSequenceNumber + 3, _fromSequenceNumber + 4
			});
		//to trigger event delivery:
		HandleEvents(_streamNames[1], 100, 101);

		Assert.AreEqual(2, HandledMessages.OfType<ReaderSubscriptionMessage.Faulted>().Count());
	}

	[Test]
	public void events_fault_message_for_out_of_sequence_events_should_be() {
		//_fromSequenceNumber+2 has been omitted
		HandleEvents(_streamNames[0],
			new long[] {
				_fromSequenceNumber, _fromSequenceNumber + 1, _fromSequenceNumber + 3, _fromSequenceNumber + 4
			});
		//to trigger event delivery:
		HandleEvents(_streamNames[1], 100, 101);

		Assert.IsTrue(HandledMessages.OfType<ReaderSubscriptionMessage.Faulted>().First().Reason
			.Contains(" was expected in the stream "));
	}
}
