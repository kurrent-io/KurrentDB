// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Plugins.Transforms;

public class ChunkDataReadStream(Stream chunkFileStream) : ChunkDataStream(chunkFileStream) {

	public sealed override bool CanRead => true;
	public sealed override bool CanSeek => true;
	public sealed override bool CanWrite => false;

	public override bool CanTimeout => true;

	public override int Read(Span<byte> buffer) => ChunkFileStream.Read(buffer);

	public sealed override void Write(ReadOnlySpan<byte> buffer)
		=> throw new NotSupportedException();

	public sealed override void Flush() => throw new NotSupportedException();
	public sealed override Task FlushAsync(CancellationToken cancellationToken)
		=> Task.FromException(new NotSupportedException());

	public sealed override void SetLength(long value) => throw new NotSupportedException();

	public override long Length => throw new NotSupportedException();

	// seeks need to support only `SeekOrigin.Begin`
	public override long Seek(long offset, SeekOrigin origin) {
		if (origin is not SeekOrigin.Begin)
			throw new NotSupportedException();

		return ChunkFileStream.Seek(offset, origin);
	}

	public override long Position {
		get => ChunkFileStream.Position;
		set => ChunkFileStream.Position = value;
	}

	protected override void Dispose(bool disposing) {
		try {
			if (!disposing)
				return;

			ChunkFileStream.Dispose();
		} finally {
			base.Dispose(disposing);
		}
	}
}
