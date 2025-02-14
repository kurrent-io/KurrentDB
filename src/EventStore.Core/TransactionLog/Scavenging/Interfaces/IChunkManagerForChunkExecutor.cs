// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;

namespace EventStore.Core.TransactionLog.Scavenging.Interfaces;

public interface IChunkManagerForChunkExecutor<TStreamId, TRecord, TChunk> where TChunk : IChunkBlob {
	ValueTask<IChunkWriterForExecutor<TStreamId, TRecord, TChunk>> CreateChunkWriter(
		IChunkReaderForExecutor<TStreamId, TRecord> sourceChunk,
		CancellationToken token);

	IChunkReaderForExecutor<TStreamId, TRecord> GetChunkReaderFor(long position);

	ValueTask<string> SwitchInTempChunk(TChunk chunk, CancellationToken token);
}

public interface IChunkManagerForChunkRemover {
	ValueTask<bool> SwitchInChunks(IReadOnlyList<string> locators, CancellationToken token);
}

public interface IChunkWriterForExecutor<TStreamId, TRecord, TChunk> where TChunk : IChunkBlob {
	string LocalFileName { get; }

	ValueTask WriteRecord(RecordForExecutor<TStreamId, TRecord> record, CancellationToken token);

	ValueTask<TChunk> Complete(CancellationToken token);

	void Abort(bool deleteImmediately);
}

public interface IChunkReaderForExecutor<TStreamId, TRecord> {
	string Name { get; }
	int FileSize { get; }
	int ChunkStartNumber { get; }
	int ChunkEndNumber { get; }
	bool IsReadOnly { get; }
	bool IsRemote { get; }
	long ChunkStartPosition { get; }
	long ChunkEndPosition { get; }
	IAsyncEnumerable<bool> ReadInto(
		RecordForExecutor<TStreamId, TRecord>.NonPrepare nonPrepare,
		RecordForExecutor<TStreamId, TRecord>.Prepare prepare,
		CancellationToken token);
}
