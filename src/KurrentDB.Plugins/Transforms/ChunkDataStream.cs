// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Runtime.CompilerServices;

namespace EventStore.Plugins.Transforms;

public abstract class ChunkDataStream(Stream chunkFileStream) : Stream {
	public Stream ChunkFileStream => chunkFileStream;

	public override int Read(Span<byte> buffer) => chunkFileStream.Read(buffer);

	public sealed override int Read(byte[] buffer, int offset, int count) {
		ValidateBufferArguments(buffer, offset, count);

		return Read(buffer.AsSpan(offset, count));
	}

	public sealed override int ReadByte() {
		Unsafe.SkipInit(out byte b);

		return Read(new(ref b)) > 0 ? b : -1;
	}

	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) =>
		chunkFileStream.ReadAsync(buffer, token);

	public sealed override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
		=> ReadAsync(buffer.AsMemory(offset, count), token).AsTask();

	public override void Write(ReadOnlySpan<byte> buffer) => ChunkFileStream.Write(buffer);

	public sealed override void Write(byte[] buffer, int offset, int count) {
		ValidateBufferArguments(buffer, offset, count);

		Write(new(buffer, offset, count));
	}

	public sealed override void WriteByte(byte value) => Write(new(ref value));

	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default)
		=> chunkFileStream.WriteAsync(buffer, token);

	public sealed override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token)
		=> WriteAsync(buffer.AsMemory(offset, count), token).AsTask();

	protected override void Dispose(bool disposing) {
		if (disposing)
			ChunkFileStream.Dispose();

		base.Dispose(disposing);
	}
}
