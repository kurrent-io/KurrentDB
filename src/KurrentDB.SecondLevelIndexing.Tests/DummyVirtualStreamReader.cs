// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage.InMemory;

namespace KurrentDB.SecondLevelIndexing.Tests;

public class DummyVirtualStreamReader(string streamName, ResolvedEvent[] events) : IVirtualStreamReader {
	public ValueTask<ClientMessage.ReadStreamEventsForwardCompleted> ReadForwards(
		ClientMessage.ReadStreamEventsForward msg,
		CancellationToken token) {

		ReadStreamResult result;
		long nextEventNumber, lastEventNumber;

		var readEvents = events
			.Where(e =>  e.Event.EventNumber >= msg.FromEventNumber)
			.ToArray();

		ResolvedEvent? lastEvent = readEvents.Length > 0 ? readEvents.Last(): null;

		if (lastEvent == null) {
			result = ReadStreamResult.NoStream;
			nextEventNumber = -1;
			lastEventNumber = ExpectedVersion.NoStream;
		} else {
			result = ReadStreamResult.Success;
			lastEventNumber = lastEvent.Value.Event.EventNumber;
			nextEventNumber =  lastEventNumber + 1;
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
			isEndOfStream: true,
			tfLastCommitPosition: 0L));
	}

	public ValueTask<ClientMessage.ReadStreamEventsBackwardCompleted> ReadBackwards(
		ClientMessage.ReadStreamEventsBackward msg,
		CancellationToken token) {

		ReadStreamResult result;
		long nextEventNumber, lastEventNumber;

		var readEvents = events
			.Where(e =>  e.Event.EventNumber <= msg.FromEventNumber)
			.ToArray();

		ResolvedEvent? lastEvent = readEvents.Length > 0 ? readEvents.First(): null;

		if (lastEvent == null) {
			result = ReadStreamResult.NoStream;
			nextEventNumber = -1;
			lastEventNumber = ExpectedVersion.NoStream;
		} else {
			result = ReadStreamResult.Success;
			lastEventNumber = lastEvent.Value.Event.EventNumber;
			nextEventNumber =  lastEventNumber - 1;
		}

		return ValueTask.FromResult(new ClientMessage.ReadStreamEventsBackwardCompleted(
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
			isEndOfStream: true,
			tfLastCommitPosition: 0L));
	}

	public long GetLastEventNumber(string streamId) =>
		events.Length > 0 ? events.Last().Event.EventNumber : -1;

	public long GetLastIndexedPosition(string streamId) =>
		events.Length > 0 ? events.Last().Event.LogPosition : -1;

	public bool CanReadStream(string streamId) =>
		streamId == streamName;

	public void Write(string eventType, ReadOnlyMemory<byte> data) { }
}
