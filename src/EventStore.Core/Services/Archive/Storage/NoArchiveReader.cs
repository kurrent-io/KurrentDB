// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services.Archive.Storage.Exceptions;

namespace EventStore.Core.Services.Archive.Storage;

// Reader for when there is no archive
public class NoArchiveReader : IArchiveStorageReader {
	private NoArchiveReader() { }

	public static NoArchiveReader Instance { get; } = new();

	public ValueTask<long> GetCheckpoint(CancellationToken ct) =>
		ValueTask.FromResult<long>(0);

	public ValueTask<int> ReadAsync(int logicalChunkNumber, Memory<byte> buffer, long offset, CancellationToken ct) =>
		ValueTask.FromException<int>(new ChunkDeletedException());

	public ValueTask<ArchivedChunkMetadata> GetMetadataAsync(int logicalChunkNumber, CancellationToken token) =>
		ValueTask.FromException<ArchivedChunkMetadata>(new ChunkDeletedException());
}
