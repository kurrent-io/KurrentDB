// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;

namespace EventStore.Core.Services.Archive.Storage;

public interface IArchiveStorage : IArchiveStorageReader {
	/// <summary>Sets the position in the transaction log up to which all chunks have been archived</summary>
	public ValueTask SetCheckpoint(long checkpoint, CancellationToken ct);

	/// <summary>Stores a chunk in the archive</summary>
	public ValueTask StoreChunk(IChunkBlob chunk, CancellationToken ct);
}
