// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using EventStore.Plugins.Transforms;

namespace EventStore.Core.Transforms.Identity;

public sealed class IdentityChunkWriteTransform : IChunkWriteTransform {
	private ChunkDataWriteStream _stream;

	public ChunkDataWriteStream TransformData(ChunkDataWriteStream stream) {
		_stream = stream;
		return _stream;
	}

	public void CompleteData(int footerSize, int alignmentSize) {
		var chunkHeaderAndDataSize = (int)_stream.Position;
		var alignedSize = GetAlignedSize(chunkHeaderAndDataSize + footerSize, alignmentSize);
		var paddingSize = alignedSize - chunkHeaderAndDataSize - footerSize;
		if (paddingSize > 0)
			_stream.Write(new byte[paddingSize]);
	}

	public void WriteFooter(ReadOnlySpan<byte> footer, out int fileSize) {
		_stream.ChunkFileStream.Write(footer);
		fileSize = (int)_stream.Length;
	}

	private static int GetAlignedSize(int size, int alignmentSize) {
		if (size % alignmentSize == 0)
			return size;
		return (size / alignmentSize + 1) * alignmentSize;
	}
}
