// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Transforms;

namespace KurrentDB.Security.EncryptionAtRest.Transforms.AesGcm;

public sealed class AesGcmChunkWriteTransform(ReadOnlyMemory<byte> key, int transformHeaderSize, IChunkReadTransform readTransform) : IChunkWriteTransform {
	private AesGcmChunkWriteStream _transformedStream;

	public ChunkDataWriteStream TransformData(ChunkDataWriteStream dataStream) {
		ArgumentNullException.ThrowIfNull(dataStream);

		_transformedStream = new AesGcmChunkWriteStream(dataStream, key.Span, transformHeaderSize, readTransform);
		return _transformedStream;
	}

	public async ValueTask CompleteData(int footerSize, int alignmentSize, CancellationToken ct) {
		await _transformedStream.WriteLastBlock(commit: true, ct);

		var chunkHeaderAndDataSize = (int)_transformedStream.ChunkFileStream.Position;
		var alignedSize = GetAlignedSize(chunkHeaderAndDataSize + footerSize, alignmentSize);
		var paddingSize = alignedSize - chunkHeaderAndDataSize - footerSize;
		if (paddingSize > 0) {
			var padding = new byte[paddingSize];
			await _transformedStream.ChunkFileStream.WriteAsync(padding, ct);
			_transformedStream.ChecksumAlgorithm.AppendData(padding);
		}
	}

	public async ValueTask<int> WriteFooter(ReadOnlyMemory<byte> footer, CancellationToken ct) {
		await _transformedStream.ChunkFileStream.WriteAsync(footer, ct);
		await _transformedStream.ChunkFileStream.FlushAsync(ct);
		return (int)_transformedStream.ChunkFileStream.Position;
	}

	private static int GetAlignedSize(int size, int alignmentSize) {
		if (size % alignmentSize == 0)
			return size;
		return (size / alignmentSize + 1) * alignmentSize;
	}
}
