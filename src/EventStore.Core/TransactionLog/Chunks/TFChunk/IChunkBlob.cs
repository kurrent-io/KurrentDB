// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EventStore.Core.TransactionLog.Chunks.TFChunk;

/// <summary>
/// Interprets database chunk as a blob.
/// </summary>
public interface IChunkBlob : IDisposable {
	string ChunkLocator { get; }

	ValueTask<IChunkRawReader> AcquireRawReader(CancellationToken token);

	IAsyncEnumerable<IChunkBlob> UnmergeAsync(CancellationToken token);

	void MarkForDeletion();

	ValueTask EnsureInitialized(CancellationToken token);

	ChunkHeader ChunkHeader { get; }

	ChunkFooter ChunkFooter { get; }
}
