// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Plugins.Transforms;

namespace EventStore.Core.Tests.Transforms.BitFlip;

public class BitFlipChunkWriteTransform : IChunkWriteTransform {
	private BitFlipChunkWriteStream _transformedStream;

	public ChunkDataWriteStream TransformData(ChunkDataWriteStream dataStream) {
		_transformedStream = new BitFlipChunkWriteStream(dataStream);
		return _transformedStream;
	}

	public void CompleteData(int footerSize, int alignmentSize) {
		var chunkHeaderAndDataSize = (int)_transformedStream.ChunkFileStream.Position;
		var alignedSize = GetAlignedSize(chunkHeaderAndDataSize + footerSize, alignmentSize);
		var paddingSize = alignedSize - chunkHeaderAndDataSize - footerSize;
		if (paddingSize > 0) {
			var padding = new byte[paddingSize];
			_transformedStream.ChunkFileStream.Write(padding);
			_transformedStream.ChecksumAlgorithm.TransformBlock(padding, 0, padding.Length, null, 0);
		}
	}

	public void WriteFooter(ReadOnlySpan<byte> footer, out int fileSize) {
		_transformedStream.ChunkFileStream.Write(footer);
		fileSize = (int)_transformedStream.ChunkFileStream.Length;
	}

	private static int GetAlignedSize(int size, int alignmentSize) {
		if (size % alignmentSize == 0)
			return size;
		return (size / alignmentSize + 1) * alignmentSize;
	}
}
