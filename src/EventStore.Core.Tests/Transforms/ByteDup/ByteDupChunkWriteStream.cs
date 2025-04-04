// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Transforms;

namespace EventStore.Core.Tests.Transforms.ByteDup;
public class ByteDupChunkWriteStream(ChunkDataWriteStream stream) :
	ChunkDataWriteStream(stream.ChunkFileStream, stream.ChecksumAlgorithm) {
	private const int HeaderSize = 128;

	public override void Write(ReadOnlySpan<byte> buffer) {
		var buf = new byte[buffer.Length * 2];
		for (int i = 0; i < buffer.Length; i++)
			buf[i * 2] = buf[i * 2 + 1] = buffer[i];

		base.Write(buf);
	}

	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) {
		var buf = new byte[buffer.Length * 2];
		for (int i = 0; i < buffer.Length; i++)
			buf[i * 2] = buf[i * 2 + 1] = buffer.Span[i];

		return base.WriteAsync(buf, token);
	}

	private static long TransformPosition(long position) => HeaderSize + (position - HeaderSize) * 2L;
	private static long UntransformPosition(long position) => HeaderSize + (position - HeaderSize) / 2L;
	public override long Position {
		get => UntransformPosition(base.Position);
		set => base.Position = TransformPosition(value);
	}

	public override void SetLength(long value) => ChunkFileStream.SetLength(TransformPosition(value));
	public override long Length => UntransformPosition(ChunkFileStream.Length);
}
