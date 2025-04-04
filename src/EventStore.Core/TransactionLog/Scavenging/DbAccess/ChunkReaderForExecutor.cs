// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Core.TransactionLog.LogRecords;
using EventStore.Core.TransactionLog.Scavenging.Interfaces;
using EventStore.LogCommon;

namespace EventStore.Core.TransactionLog.Scavenging.DbAccess;

public class ChunkReaderForExecutor<TStreamId> : IChunkReaderForExecutor<TStreamId, ILogRecord> {
	private readonly TFChunk _chunk;

	public ChunkReaderForExecutor(TFChunk chunk) {
		_chunk = chunk;
	}

	public string Name => _chunk.ToString();

	public int FileSize => _chunk.FileSize;

	public int ChunkStartNumber => _chunk.ChunkHeader.ChunkStartNumber;

	public int ChunkEndNumber => _chunk.ChunkHeader.ChunkEndNumber;

	public bool IsReadOnly => _chunk.IsReadOnly;

	public bool IsRemote => _chunk.IsRemote;

	public long ChunkStartPosition => _chunk.ChunkHeader.ChunkStartPosition;

	public long ChunkEndPosition => _chunk.ChunkHeader.ChunkEndPosition;

	// similar to TFChunkScavenger.TraverseChunkBasic
	public async IAsyncEnumerable<bool> ReadInto(
		RecordForExecutor<TStreamId, ILogRecord>.NonPrepare nonPrepare,
		RecordForExecutor<TStreamId, ILogRecord>.Prepare prepare,
		[EnumeratorCancellation] CancellationToken token) {

		var result = await _chunk.TryReadFirst(token);
		while (result.Success) {
			var record = result.LogRecord;
			if (record.RecordType != LogRecordType.Prepare) {
				nonPrepare.SetRecord(result.RecordLength, record);
				yield return false;
			} else {
				var sourcePrepare = record as IPrepareLogRecord<TStreamId>;
				prepare.SetRecord(
					length: result.RecordLength,
					logPosition: record.LogPosition,
					record: record,
					timeStamp: sourcePrepare.TimeStamp,
					streamId: sourcePrepare.EventStreamId,
					isSelfCommitted: sourcePrepare.Flags.HasAnyOf(PrepareFlags.IsCommitted),
					isTombstone: sourcePrepare.Flags.HasAnyOf(PrepareFlags.StreamDelete),
					isTransactionBegin: sourcePrepare.Flags.HasAnyOf(PrepareFlags.TransactionBegin),
					eventNumber: sourcePrepare.ExpectedVersion + 1);
				yield return true;
			}

			result = await _chunk.TryReadClosestForward(result.NextPosition, token);
		}
	}
}
