// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using KurrentDB.Core.Data;
using static KurrentDB.Core.Messages.ClientMessage;

namespace KurrentDB.Core.Services.Storage.InMemory;

public class VirtualStreamReader : IVirtualStreamReader {
	private readonly IVirtualStreamReader[] _readers;

	public VirtualStreamReader(IVirtualStreamReader[] readers) {
		_readers = readers;
	}

	bool TryGetReader(string streamId, out IVirtualStreamReader reader) {
		for (var i = 0; i < _readers.Length; i++) {
			if (!_readers[i].OwnStream(streamId)) continue;
			reader = _readers[i];
			return true;
		}

		reader = null;
		return false;
	}

	public ValueTask<ReadStreamEventsForwardCompleted> ReadForwards(ReadStreamEventsForward msg, CancellationToken token) {
		if (TryGetReader(msg.EventStreamId, out var reader))
			return reader.ReadForwards(msg, token);

		return ValueTask.FromResult(new ReadStreamEventsForwardCompleted(
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
			tfLastCommitPosition: -1));
	}

	public ValueTask<ReadStreamEventsBackwardCompleted> ReadBackwards(ReadStreamEventsBackward msg, CancellationToken token) {
		if (TryGetReader(msg.EventStreamId, out var reader))
			return reader.ReadBackwards(msg, token);

		return ValueTask.FromResult(new ReadStreamEventsBackwardCompleted(
			msg.CorrelationId,
			msg.EventStreamId,
			msg.FromEventNumber,
			msg.MaxCount,
			ReadStreamResult.NoStream,
			[],
			streamMetadata: StreamMetadata.Empty,
			isCachePublic: false,
			error: string.Empty,
			nextEventNumber: -1,
			lastEventNumber: ExpectedVersion.NoStream,
			isEndOfStream: true,
			tfLastCommitPosition: -1));
	}

	public long GetLastEventNumber(string streamId) {
		return TryGetReader(streamId, out var reader) ? reader.GetLastEventNumber(streamId) : -1;
	}

	public long GetLastIndexedPosition(string streamId) {
		return TryGetReader(streamId, out var reader) ? reader.GetLastIndexedPosition(streamId) : -1;
	}

	public bool OwnStream(string streamId) => TryGetReader(streamId, out _);
}
