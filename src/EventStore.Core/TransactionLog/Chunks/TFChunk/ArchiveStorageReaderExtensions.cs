// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services.Archive.Storage;

namespace EventStore.Core.TransactionLog.Chunks.TFChunk;

public static class ArchiveStorageReaderExtensions {
	public static ValueTask<IChunkHandle> OpenForReadAsync(this IArchiveStorageReader self, int chunkNumber, CancellationToken token) =>
		ArchivedChunkHandle.OpenForReadAsync(self, chunkNumber, token);
}
