// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Transforms;

namespace KurrentDB.Core.Tests.Transforms.WithHeader;
public class WithHeaderChunkWriteTransform(int transformHeaderSize) : IChunkWriteTransform {
	private ChunkDataWriteStream _transformedStream;

	public ChunkDataWriteStream TransformData(ChunkDataWriteStream stream) {
		_transformedStream = new WithHeaderChunkWriteStream(stream, transformHeaderSize);
		return _transformedStream;
	}

	public async ValueTask CompleteData(int footerSize, int alignmentSize, CancellationToken token) {
		var chunkHeaderAndDataSize = (int)_transformedStream.ChunkFileStream.Position;
		var alignedSize = GetAlignedSize(chunkHeaderAndDataSize + footerSize, alignmentSize);
		var paddingSize = alignedSize - chunkHeaderAndDataSize - footerSize;
		if (paddingSize > 0) {
			var padding = new byte[paddingSize];
			await _transformedStream.ChunkFileStream.WriteAsync(padding, token);
			_transformedStream.ChecksumAlgorithm.AppendData(padding);
		}
	}

	public async ValueTask<int> WriteFooter(ReadOnlyMemory<byte> footer, CancellationToken token) {
		await _transformedStream.ChunkFileStream.WriteAsync(footer, token);
		await _transformedStream.FlushAsync(token);
		return (int)_transformedStream.Position;
	}

	private static int GetAlignedSize(int size, int alignmentSize) {
		if (size % alignmentSize == 0)
			return size;
		return (size / alignmentSize + 1) * alignmentSize;
	}
}
