// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Transforms;

namespace KurrentDB.Core.Tests.Transforms.BitFlip;

public class BitFlipChunkWriteStream(ChunkDataWriteStream stream) :
	ChunkDataWriteStream(stream.ChunkFileStream, stream.ChecksumAlgorithm) {

	public override void Write(ReadOnlySpan<byte> buffer) {
		var tmp = new byte[buffer.Length];
		FlipBits(buffer, tmp);

		base.Write(tmp);
	}

	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) {
		var tmp = new byte[buffer.Length];
		FlipBits(buffer.Span, tmp);

		return base.WriteAsync(tmp, token);
	}

	private static void FlipBits(ReadOnlySpan<byte> source, Span<byte> destination) {
		for (int i = 0; i < source.Length; i++)
			destination[i] = (byte)(source[i] ^ 0xFF);
	}
}
