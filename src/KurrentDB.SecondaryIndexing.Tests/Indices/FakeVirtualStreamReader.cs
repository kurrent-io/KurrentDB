// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage.InMemory;

namespace KurrentDB.SecondaryIndexing.Tests.Indices;

internal class FakeVirtualStreamReader(string streamName, IReadOnlyList<ResolvedEvent> events) : IVirtualStreamReader {
	public ValueTask<ClientMessage.ReadStreamEventsForwardCompleted> ReadForwards(
		ClientMessage.ReadStreamEventsForward msg,
		CancellationToken token) {

		ReadStreamResult result;
		long nextEventNumber, lastEventNumber, lastPosition;
		bool isEndOfStream;

		var readEvents = events
			.Skip((int)msg.FromEventNumber)
			.ToArray();

		ResolvedEvent? lastEvent = readEvents.Length > 0 ? readEvents.Last(): null;

		if (lastEvent == null) {
			result = ReadStreamResult.NoStream;
			nextEventNumber = -1;
			lastEventNumber = ExpectedVersion.NoStream;
			lastPosition = -1;
			isEndOfStream = true;
		} else {
			result = ReadStreamResult.Success;
			lastEventNumber = Array.IndexOf(readEvents, lastEvent);
			nextEventNumber =  lastEventNumber + 1;
			lastPosition = lastEvent.Value.Event.TransactionPosition;
			isEndOfStream = nextEventNumber == events.Count;
		}

		return ValueTask.FromResult(new ClientMessage.ReadStreamEventsForwardCompleted(
			msg.CorrelationId,
			msg.EventStreamId,
			msg.FromEventNumber,
			msg.MaxCount,
			result,
			readEvents,
			StreamMetadata.Empty,
			isCachePublic: false,
			error: string.Empty,
			nextEventNumber: nextEventNumber,
			lastEventNumber: lastEventNumber,
			isEndOfStream: isEndOfStream,
			tfLastCommitPosition: lastPosition));
	}

	public ValueTask<ClientMessage.ReadStreamEventsBackwardCompleted> ReadBackwards(
		ClientMessage.ReadStreamEventsBackward msg,
		CancellationToken token) {

		ReadStreamResult result;
		long nextEventNumber, lastEventNumber, lastPosition;

		var readEvents = events
			.Reverse()
			.Skip((int)msg.FromEventNumber)
			.ToArray();

		ResolvedEvent? lastEvent = readEvents.Length > 0 ? readEvents.First(): null;

		if (lastEvent == null) {
			result = ReadStreamResult.NoStream;
			nextEventNumber = -1;
			lastEventNumber = ExpectedVersion.NoStream;
			lastPosition = -1;
		} else {
			result = ReadStreamResult.Success;
			lastEventNumber = Array.IndexOf(readEvents, lastEvent);
			nextEventNumber =  lastEventNumber - 1;
			lastPosition = lastEvent.Value.Event.LogPosition;
		}

		return ValueTask.FromResult(new ClientMessage.ReadStreamEventsBackwardCompleted(
			msg.CorrelationId,
			msg.EventStreamId,
			msg.FromEventNumber,
			msg.MaxCount,
			result,
			readEvents.Reverse().ToArray(),
			StreamMetadata.Empty,
			isCachePublic: false,
			error: string.Empty,
			nextEventNumber: nextEventNumber,
			lastEventNumber: lastEventNumber,
			isEndOfStream: lastEventNumber == 0,
			tfLastCommitPosition: lastPosition));
	}

	public long GetLastEventNumber(string streamId) =>
		events.Count > 0 ? events[^1].Event.EventNumber : -1;

	public long GetLastIndexedPosition(string streamId) =>
		events.Count > 0 ? events[^1].Event.LogPosition : -1;

	public bool CanReadStream(string streamId) =>
		streamId == streamName;

	public void Write(string eventType, ReadOnlyMemory<byte> data) { }
}
