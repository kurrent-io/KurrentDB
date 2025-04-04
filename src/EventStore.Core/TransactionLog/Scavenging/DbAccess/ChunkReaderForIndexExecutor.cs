// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;

namespace EventStore.Core.TransactionLog.Scavenging.DbAccess;

public class ChunkReaderForIndexExecutor<TStreamId> : IChunkReaderForIndexExecutor<TStreamId> {
	private readonly Func<TFReaderLease> _tfReaderFactory;

	public ChunkReaderForIndexExecutor(Func<TFReaderLease> tfReaderFactory) {
		_tfReaderFactory = tfReaderFactory;
	}

	public async ValueTask<Optional<TStreamId>> TryGetStreamId(long position, CancellationToken token) {
		using var reader = _tfReaderFactory();
		var result = await reader.TryReadAt(position, couldBeScavenged: true, token);
		if (!result.Success) {
			return Optional.None<TStreamId>();
		}

		if (result.LogRecord is not IPrepareLogRecord<TStreamId> prepare)
			throw new Exception($"Record in index at position {position} is not a prepare");

		return prepare.EventStreamId;
	}
}
