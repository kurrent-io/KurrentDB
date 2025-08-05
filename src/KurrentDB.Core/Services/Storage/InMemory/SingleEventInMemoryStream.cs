// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Bus;
using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.TransactionLog.LogRecords;

namespace KurrentDB.Core.Services.Storage.InMemory;

// threading: we expect to handle one Write at a time, but Reads can happen concurrently
// with the write and with other reads.
public class SingleEventInMemoryStream : IVirtualStreamReader {
	private readonly IPublisher _publisher;
	private readonly InMemoryLog _memLog;
	private readonly string _streamName;
	private const PrepareFlags Flags = PrepareFlags.Data | PrepareFlags.IsCommitted | PrepareFlags.IsJson;
	private long _eventNumber;
	private EventRecord _lastEvent;

	public SingleEventInMemoryStream(IPublisher publisher, InMemoryLog memLog, string streamName) {
		_publisher = publisher;
		_eventNumber = 0;
		_memLog = memLog;
		_streamName = streamName;
	}

	public ValueTask<ClientMessage.ReadStreamEventsForwardCompleted> ReadForwards(
		ClientMessage.ReadStreamEventsForward msg,
		CancellationToken token) {

		ReadStreamResult result;
		ResolvedEvent[] events;
		long nextEventNumber, lastEventNumber;

		var lastEvent = _lastEvent;
		if (lastEvent == null) {
			// no stream
			result = ReadStreamResult.NoStream;
			events = [];
			nextEventNumber = -1;
			lastEventNumber = ExpectedVersion.NoStream;
		} else {
			result = ReadStreamResult.Success;
			nextEventNumber = lastEvent.EventNumber + 1;
			lastEventNumber = lastEvent.EventNumber;

			if (msg.FromEventNumber > lastEvent.EventNumber) {
				// from too high. empty read
				events = [];
			} else {
				// read containing the event
				events = [ResolvedEvent.ForUnresolvedEvent(lastEvent)];
			}
		}

		return ValueTask.FromResult(new ClientMessage.ReadStreamEventsForwardCompleted(
			msg.CorrelationId,
			msg.EventStreamId,
			msg.FromEventNumber,
			msg.MaxCount,
			result,
			events,
			StreamMetadata.Empty,
			isCachePublic: false,
			error: string.Empty,
			nextEventNumber: nextEventNumber,
			lastEventNumber: lastEventNumber,
			isEndOfStream: true,
			tfLastCommitPosition: _memLog.GetLastCommitPosition()));
	}

	public ValueTask<ClientMessage.ReadStreamEventsBackwardCompleted> ReadBackwards(
		ClientMessage.ReadStreamEventsBackward msg,
		CancellationToken token) {

		ReadStreamResult result;
		ResolvedEvent[] events;
		long adjustedFromEventNumber, lastEventNumber;

		var lastEvent = _lastEvent;
		if (lastEvent == null) {
			// no stream
			adjustedFromEventNumber = msg.FromEventNumber;
			result = ReadStreamResult.NoStream;
			events = [];
			lastEventNumber = ExpectedVersion.NoStream;
		} else {
			result = ReadStreamResult.Success;
			lastEventNumber = lastEvent.EventNumber;

			var readFromEnd = msg.FromEventNumber < 0;
			adjustedFromEventNumber = readFromEnd ? lastEvent.EventNumber : msg.FromEventNumber;

			if (adjustedFromEventNumber < lastEvent.EventNumber) {
				// from too low. empty read
				events = [];
			} else {
				// read containing the event
				events = [ResolvedEvent.ForUnresolvedEvent(lastEvent)];
			}
		}

		return ValueTask.FromResult(new ClientMessage.ReadStreamEventsBackwardCompleted(
			correlationId: msg.CorrelationId,
			eventStreamId: msg.EventStreamId,
			fromEventNumber: adjustedFromEventNumber,
			maxCount: msg.MaxCount,
			result: result,
			events: events,
			streamMetadata: StreamMetadata.Empty,
			isCachePublic: false,
			error: string.Empty,
			nextEventNumber: -1,
			lastEventNumber: lastEventNumber,
			isEndOfStream: true,
			tfLastCommitPosition: _memLog.GetLastCommitPosition()));
	}

	public long GetLastEventNumber(string streamId) => _lastEvent?.EventNumber ?? -1;

	public long GetLastIndexedPosition(string streamId) => -1;

	public bool CanReadStream(string streamId) => streamId == _streamName;

	public void Write(string eventType, ReadOnlyMemory<byte> data) {
		var commitPosition = _memLog.GetNextCommitPosition();
		var prepare = new PrepareLogRecord(
			logPosition: commitPosition,
			correlationId: Guid.NewGuid(),
			eventId: Guid.NewGuid(),
			transactionPosition: commitPosition,
			transactionOffset: 0,
			eventStreamId: _streamName,
			eventStreamIdSize: null,
			expectedVersion: _eventNumber - 1,
			timeStamp: DateTime.Now,
			flags: Flags,
			eventType: eventType,
			eventTypeSize: null,
			data: data,
			metadata: Array.Empty<byte>());
		_lastEvent = new EventRecord(_eventNumber, prepare, _streamName, eventType);
		_publisher.Publish(new StorageMessage.InMemoryEventCommitted(commitPosition, _lastEvent));
		_eventNumber++;
	}
}
