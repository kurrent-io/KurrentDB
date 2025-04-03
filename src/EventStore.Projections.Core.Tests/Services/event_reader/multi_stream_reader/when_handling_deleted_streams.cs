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
using ReadStreamResult = EventStore.Core.Data.ReadStreamResult;
using ResolvedEvent = EventStore.Core.Data.ResolvedEvent;

namespace EventStore.Projections.Core.Tests.Services.event_reader.multi_stream_reader;

[TestFixture(typeof(LogFormat.V2), typeof(string))]
[TestFixture(typeof(LogFormat.V3), typeof(uint))]
public class when_handling_deleted_streams<TLogFormat, TStreamId> : TestFixtureWithExistingEvents<TLogFormat, TStreamId> {
	private MultiStreamEventReader _edp;
	private string[] _streamNames;
	private Dictionary<string, long> _streamPositions;
	private Guid _distibutionPointCorrelationId;
	private long _fromSequenceNumber = 10;

	protected override void Given() {
		TicksAreHandledImmediately();
		_streamNames = new[] {"stream1", "stream2"};
		_streamPositions = new Dictionary<string, long> {{"stream1", 10}, {"stream2", _fromSequenceNumber}};
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

	private void HandleDeletedStream(string stream, long sequenceNumber, ReadStreamResult result) {
		var correlationId = _consumer.HandledMessages.OfType<ClientMessage.ReadStreamEventsForward>()
			.Last(x => x.EventStreamId == stream).CorrelationId;

		_edp.Handle(
			new ClientMessage.ReadStreamEventsForwardCompleted(
				correlationId, stream, sequenceNumber, 100, result, new ResolvedEvent[] { }, null, false, "", -1,
				sequenceNumber, true, 200)
		);
	}

	[Test]
	public void when_no_stream_and_sequence_num_equal_to_minus_one_should_not_publish_partition_deleted_message() {
		HandleDeletedStream(_streamNames[0], -1, ReadStreamResult.NoStream);
		//trigger event delivery:
		HandleEvents(_streamNames[1], _fromSequenceNumber, _fromSequenceNumber);

		Assert.AreEqual(0,
			_consumer.HandledMessages.OfType<ReaderSubscriptionMessage.EventReaderPartitionDeleted>().Count());
	}

	[Test]
	public void when_no_stream_and_sequence_num_equal_to_zero_should_publish_partition_deleted_message() {
		HandleDeletedStream(_streamNames[0], 0, ReadStreamResult.NoStream);
		//trigger event delivery:
		HandleEvents(_streamNames[1], _fromSequenceNumber, _fromSequenceNumber);

		Assert.AreEqual(1,
			_consumer.HandledMessages.OfType<ReaderSubscriptionMessage.EventReaderPartitionDeleted>().Count());
		Assert.AreEqual(_streamNames[0],
			_consumer.HandledMessages.OfType<ReaderSubscriptionMessage.EventReaderPartitionDeleted>().First()
				.Partition);
	}

	[Test]
	public void when_no_stream_and_sequence_num_greater_than_zero_should_publish_partition_deleted_message() {
		HandleDeletedStream(_streamNames[0], 100, ReadStreamResult.NoStream);
		//trigger event delivery:
		HandleEvents(_streamNames[1], _fromSequenceNumber, _fromSequenceNumber);

		Assert.AreEqual(1,
			_consumer.HandledMessages.OfType<ReaderSubscriptionMessage.EventReaderPartitionDeleted>().Count());
		Assert.AreEqual(_streamNames[0],
			_consumer.HandledMessages.OfType<ReaderSubscriptionMessage.EventReaderPartitionDeleted>().First()
				.Partition);
	}

	[Test]
	public void when_stream_deleted_should_publish_partition_deleted_message() {
		HandleDeletedStream(_streamNames[0], 0, ReadStreamResult.StreamDeleted);
		//trigger event delivery:
		HandleEvents(_streamNames[1], _fromSequenceNumber, _fromSequenceNumber);

		Assert.AreEqual(1,
			_consumer.HandledMessages.OfType<ReaderSubscriptionMessage.EventReaderPartitionDeleted>().Count());
		Assert.AreEqual(_streamNames[0],
			_consumer.HandledMessages.OfType<ReaderSubscriptionMessage.EventReaderPartitionDeleted>().First()
				.Partition);
	}
}
