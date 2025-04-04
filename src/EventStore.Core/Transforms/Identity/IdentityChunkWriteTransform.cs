// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using DotNext.Buffers;
using EventStore.Plugins.Transforms;

namespace EventStore.Core.Transforms.Identity;

public sealed class IdentityChunkWriteTransform : IChunkWriteTransform {
	private ChunkDataWriteStream _stream;

	public ChunkDataWriteStream TransformData(ChunkDataWriteStream stream) {
		_stream = stream;
		return _stream;
	}

	public ValueTask CompleteData(int footerSize, int alignmentSize, CancellationToken token) {
		var chunkHeaderAndDataSize = (int)_stream.Position;
		var alignedSize = GetAlignedSize(chunkHeaderAndDataSize + footerSize, alignmentSize);
		var paddingSize = alignedSize - chunkHeaderAndDataSize - footerSize;
		return paddingSize > 0
			? WritePaddingAsync(_stream, paddingSize, token)
			: ValueTask.CompletedTask;

		static async ValueTask WritePaddingAsync(ChunkDataWriteStream stream, int paddingSize, CancellationToken token) {
			using var buffer = Memory.AllocateExactly<byte>(paddingSize);
			buffer.Span.Clear(); // ensure that the padding is zeroed
			await stream.WriteAsync(buffer.Memory, token);
		}
	}

	public async ValueTask<int> WriteFooter(ReadOnlyMemory<byte> footer, CancellationToken token) {
		await _stream.ChunkFileStream.WriteAsync(footer, token);

		// The underlying stream can implement buffered I/O, this is why we need to flush it
		// to get the correct length (because some bytes can be in the internal buffer)
		await _stream.ChunkFileStream.FlushAsync(token);
		return (int)_stream.Position;
	}

	private static int GetAlignedSize(int size, int alignmentSize) {
		var quotient = Math.DivRem(size, alignmentSize, out var remainder);

		return remainder is 0 ? size : (quotient + 1) * alignmentSize;
	}
}
