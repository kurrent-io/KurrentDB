// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;

namespace EventStore.Core.TransactionLog.Scavenging.Interfaces;

public interface IChunkManagerForChunkExecutor<TStreamId, TRecord> {
	IChunkFileSystem FileSystem { get; }

	ValueTask<IChunkWriterForExecutor<TStreamId, TRecord>> CreateChunkWriter(
		IChunkReaderForExecutor<TStreamId, TRecord> sourceChunk,
		CancellationToken token);

	ValueTask<IChunkReaderForExecutor<TStreamId, TRecord>> GetChunkReaderFor(long position, CancellationToken token);
}

public interface IChunkManagerForChunkRemover {
	ValueTask<bool> SwitchInChunks(IReadOnlyList<string> locators, CancellationToken token);
}

public interface IChunkWriterForExecutor<TStreamId, TRecord> {
	string LocalFileName { get; }

	ValueTask WriteRecord(RecordForExecutor<TStreamId, TRecord> record, CancellationToken token);

	ValueTask<(string NewFileName, long NewFileSize)> Complete(CancellationToken token);

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
