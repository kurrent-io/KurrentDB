// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Core.Services.Archive.Storage;
using EventStore.Core.TransactionLog.Chunks;
using EventStore.Core.TransactionLog.Chunks.TFChunk;
using EventStore.Plugins.Transforms;

namespace EventStore.Core.XUnit.Tests.Services.Archive.ArchiveCatchup;

internal class FakeArchiveStorage : IArchiveStorage {
	private readonly int _chunkSize;
	private readonly int _fileSize;
	private readonly long _checkpoint;

	private readonly Action<int> _onGetChunk;

	public int[] ChunkGets {
		get {
			lock (_chunkGets) {
				return _chunkGets.Order().ToArray();
			}
		}
	}
	private readonly List<int> _chunkGets;

	public FakeArchiveStorage(int chunkSize, long checkpoint, Action<int> onGetChunk) {
		_chunkSize = chunkSize;
		_fileSize = ChunkHeader.Size + _chunkSize;
		_checkpoint = checkpoint;
		_onGetChunk = onGetChunk;
		_chunkGets = [];
	}

	public ValueTask StoreChunk(IChunkBlob chunk, CancellationToken ct) => throw new NotImplementedException();
	public ValueTask SetCheckpoint(long checkpoint, CancellationToken ct) => throw new NotImplementedException();

	public ValueTask<long> GetCheckpoint(CancellationToken ct) {
		return ValueTask.FromResult(_checkpoint);
	}

	private ChunkHeader CreateChunkHeader(int chunkStartNumber, int chunkEndNumber) {
		return new ChunkHeader(
			version: (int) TFChunk.ChunkVersions.Transformed,
			minCompatibleVersion: (int) TFChunk.ChunkVersions.Transformed,
			chunkSize: _chunkSize,
			chunkStartNumber,
			chunkEndNumber,
			isScavenged: false,
			chunkId: Guid.NewGuid(),
			transformType: TransformType.Identity);
	}

	public ValueTask<int> ReadAsync(int logicalChunkNumber, Memory<byte> buffer, long offset, CancellationToken ct) {
		if (offset == _fileSize)
			return new(0); // end of stream

		// we expect to read the chunk in one shot
		if (offset != 0)
			throw new NotImplementedException();

		if (buffer.Length < _fileSize)
			throw new NotImplementedException();

		lock (_chunkGets) {
			_chunkGets.Add(logicalChunkNumber);
		}

		_onGetChunk?.Invoke(logicalChunkNumber);
		var header = CreateChunkHeader(logicalChunkNumber, logicalChunkNumber);
		header.Format(buffer.Span[..ChunkHeader.Size]);

		return new(_fileSize);
	}

	public ValueTask<ArchivedChunkMetadata> GetMetadataAsync(int logicalChunkNumber, CancellationToken token) {
		return ValueTask.FromResult<ArchivedChunkMetadata>(new(PhysicalSize: _fileSize));
	}
}
