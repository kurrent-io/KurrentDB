// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext;
using DotNext.Buffers;
using EventStore.Core.Services.Archive.Naming;
using EventStore.Core.Services.Archive.Storage;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Plugins.Transforms;
using static EventStore.Core.TransactionLog.Chunks.TFChunk.TFChunk;

namespace EventStore.Core.XUnit.Tests.Scavenge.Infrastructure;

// A virtual archive that stores only headers and footers.
// We can imagine that all the records have been scavenged away.
// This is used by the scavenge tests, which are not concerned with the contents of the chunks in the
// archive because the scavenge is not responsible for populating the archive.
class ArchiveReaderEmptyChunks(long chunkSize, long chunksInArchive) : IArchiveStorage {
	const int FileSize = TFConsts.ChunkHeaderSize + TFConsts.ChunkFooterSize;

	public ValueTask<long> GetCheckpoint(CancellationToken ct) => new(chunkSize * chunksInArchive);

	public ValueTask<ArchivedChunkMetadata> GetMetadataAsync(int logicalChunkNumber, CancellationToken token) =>
		ValueTask.FromResult<ArchivedChunkMetadata>(new(PhysicalSize: FileSize));

	public ValueTask<int> ReadAsync(int logicalChunkNumber, Memory<byte> buffer, long offset, CancellationToken ct) {
		if (ct.IsCancellationRequested)
			return ValueTask.FromCanceled<int>(ct);
		try {
			return ValueTask.FromResult(Read(logicalChunkNumber, buffer, offset));
		} catch (Exception ex) {
			return ValueTask.FromException<int>(ex);
		}
	}

	private int Read(int logicalChunkNumber, Memory<byte> buffer, long offset) {
		if (logicalChunkNumber >= chunksInArchive)
			throw new InvalidOperationException();

		var header = new ChunkHeader(
			version: (byte)ChunkVersions.Transformed,
			minCompatibleVersion: (byte)ChunkVersions.Transformed,
			chunkSize: 1,
			chunkStartNumber: logicalChunkNumber,
			chunkEndNumber: logicalChunkNumber,
			isScavenged: true,
			chunkId: Guid.NewGuid(),
			transformType: TransformType.Identity);

		var footer = new ChunkFooter(
			isCompleted: true,
			isMap12Bytes: true,
			physicalDataSize: 0,
			logicalDataSize: chunkSize,
			mapSize: 0);

		using var wholeChunkFile = Memory.AllocateExactly<byte>(FileSize);
		SpanWriter<byte> writer = new(wholeChunkFile.Span);
		writer.Write(header);
		writer.Write(footer);

		wholeChunkFile.Span[(int)offset..].CopyTo(buffer.Span, out var writtenCount);
		return writtenCount;
	}

	public ValueTask SetCheckpoint(long checkpoint, CancellationToken ct) {
		throw new NotImplementedException();
	}

	public ValueTask StoreChunk(IChunkBlob chunk, CancellationToken ct) {
		throw new NotImplementedException();
	}
}
