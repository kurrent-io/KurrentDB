// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Core.TransactionLog.LogRecords;

namespace EventStore.Core.TransactionLog.Scavenging;

public class ChunkReaderForIndexExecutor<TStreamId> : IChunkReaderForIndexExecutor<TStreamId> {
	private readonly Func<TFReaderLease> _tfReaderFactory;

	public ChunkReaderForIndexExecutor(Func<TFReaderLease> tfReaderFactory) {
		_tfReaderFactory = tfReaderFactory;
	}

	public bool TryGetStreamId(long position, out TStreamId streamId) {
		using (var reader = _tfReaderFactory()) {
			var result = reader.TryReadAt(position, couldBeScavenged: true);
			if (!result.Success) {
				streamId = default;
				return false;
			}

			if (result.LogRecord is not IPrepareLogRecord<TStreamId> prepare)
				throw new Exception($"Record in index at position {position} is not a prepare");

			streamId = prepare.EventStreamId;
			return true;
		}
	}
}
