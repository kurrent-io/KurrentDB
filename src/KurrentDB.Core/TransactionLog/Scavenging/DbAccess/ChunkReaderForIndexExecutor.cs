// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using KurrentDB.Core.TransactionLog.LogRecords;
using KurrentDB.Core.TransactionLog.Scavenging.Interfaces;

namespace KurrentDB.Core.TransactionLog.Scavenging.DbAccess;

public class ChunkReaderForIndexExecutor<TStreamId> : IChunkReaderForIndexExecutor<TStreamId> {
	private readonly ITransactionFileReader _reader;

	public ChunkReaderForIndexExecutor(ITransactionFileReader tfReader)
		=> _reader = tfReader;

	public async ValueTask<Optional<TStreamId>> TryGetStreamId(long position, CancellationToken token) {
		var result = await _reader.TryReadAt(position, couldBeScavenged: true, token);
		if (!result.Success) {
			return Optional.None<TStreamId>();
		}

		if (result.LogRecord is not IPrepareLogRecord<TStreamId> prepare)
			throw new Exception($"Record in index at position {position} is not a prepare");

		return prepare.EventStreamId;
	}
}
