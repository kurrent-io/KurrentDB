// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Buffers;
using DotNext.IO;
using EventStore.Core.Services.Archive.Naming;
using EventStore.Core.Services.Archive.Storage.Exceptions;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using Serilog;

namespace EventStore.Core.Services.Archive.Storage;

public class ArchiveStorage(
	IBlobStorage blobStorage,
	IArchiveChunkNameResolver chunkNameResolver,
	string archiveCheckpointFile)
	: IArchiveStorage {

	private static readonly ILogger Log = Serilog.Log.ForContext<ArchiveStorage>();

	public async ValueTask<long> GetCheckpoint(CancellationToken ct) {
		try {
			using var buffer = Memory.AllocateExactly<byte>(sizeof(long));
			await blobStorage.ReadAsync(archiveCheckpointFile, buffer.Memory, offset: 0, ct);
			var checkpoint = BinaryPrimitives.ReadInt64LittleEndian(buffer.Span);
			return checkpoint;
		} catch (FileNotFoundException) {
			return 0;
		}
	}

	public async ValueTask<int> ReadAsync(int logicalChunkNumber, Memory<byte> buffer, long offset, CancellationToken ct) {
		try {
			var chunkFile = chunkNameResolver.ResolveFileName(logicalChunkNumber);
			return await blobStorage.ReadAsync(chunkFile, buffer, offset, ct);
		} catch (FileNotFoundException) {
			throw new ChunkDeletedException();
		}
	}

	public async ValueTask<ArchivedChunkMetadata> GetMetadataAsync(int logicalChunkNumber, CancellationToken ct) {
		try {
			var objectName = chunkNameResolver.ResolveFileName(logicalChunkNumber);
			var metadata = await blobStorage.GetMetadataAsync(objectName, ct);
			return new(PhysicalSize: metadata.Size);
		} catch (FileNotFoundException) {
			throw new ChunkDeletedException();
		}
	}

	public async ValueTask SetCheckpoint(long checkpoint, CancellationToken ct) {
		var buffer = Memory.AllocateExactly<byte>(sizeof(long));
		var stream = StreamSource.AsStream(buffer.Memory);
		try {
			BinaryPrimitives.WriteInt64LittleEndian(buffer.Span, checkpoint);
			await blobStorage.StoreAsync(stream, archiveCheckpointFile, ct);
		} finally {
			await stream.DisposeAsync();
			buffer.Dispose();
		}
	}

	public async ValueTask StoreChunk(IChunkBlob chunk, CancellationToken ct) {
		// process single chunk
		if (chunk.ChunkHeader.IsSingleLogicalChunk) {
			await StoreAsync(chunk, ct);
		} else {
			await foreach (var unmergedChunk in chunk.UnmergeAsync().WithCancellation(ct)) {
				// we need to dispose the unmerged chunk because it's temporary chunk
				using (unmergedChunk) {
					await StoreAsync(unmergedChunk, ct);
				}
			}
		}
	}

	private async ValueTask StoreAsync(IChunkBlob chunk, CancellationToken ct) {
		Debug.Assert(chunk.ChunkHeader.IsSingleLogicalChunk);

		var name = chunkNameResolver.ResolveFileName(chunk.ChunkHeader.ChunkStartNumber);
		using var reader = await chunk.AcquireRawReader(ct);
		reader.Stream.Position = 0L;
		await blobStorage.StoreAsync(reader.Stream, name, ct);
	}
}
