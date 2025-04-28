// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Data;
using KurrentDB.Core.Messages;
using KurrentDB.Core.Services.Storage.InMemory;

namespace KurrentDB.SecondLevelIndexing;

public class DummyVirtualStreamReader(string streamName) : IVirtualStreamReader {
	public ValueTask<ClientMessage.ReadStreamEventsForwardCompleted> ReadForwards(
		ClientMessage.ReadStreamEventsForward msg,
		CancellationToken token) =>
		ValueTask.FromResult(new ClientMessage.ReadStreamEventsForwardCompleted(
			msg.CorrelationId,
			msg.EventStreamId,
			msg.FromEventNumber,
			msg.MaxCount,
			ReadStreamResult.NoStream,
			[],
			StreamMetadata.Empty,
			isCachePublic: false,
			error: string.Empty,
			nextEventNumber: -1,
			lastEventNumber: ExpectedVersion.NoStream,
			isEndOfStream: true,
			tfLastCommitPosition: 0L));

	public ValueTask<ClientMessage.ReadStreamEventsBackwardCompleted> ReadBackwards(
		ClientMessage.ReadStreamEventsBackward msg,
		CancellationToken token) =>
		ValueTask.FromResult(new ClientMessage.ReadStreamEventsBackwardCompleted(
			correlationId: msg.CorrelationId,
			eventStreamId: msg.EventStreamId,
			fromEventNumber: msg.FromEventNumber,
			maxCount: msg.MaxCount,
			result: ReadStreamResult.NoStream,
			events: [],
			streamMetadata: StreamMetadata.Empty,
			isCachePublic: false,
			error: string.Empty,
			nextEventNumber: -1,
			lastEventNumber: ExpectedVersion.NoStream,
			isEndOfStream: true,
			tfLastCommitPosition: 0L));

	public long GetLastEventNumber(string streamId) => -1;

	public long GetLastIndexedPosition(string streamId) => -1;

	public bool CanReadStream(string streamId) => streamId == streamName;

	public void Write(string eventType, ReadOnlyMemory<byte> data) { }
}
