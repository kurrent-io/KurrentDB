// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Plugins.Transforms;

namespace EventStore.Core.Tests.Transforms.BitFlip;

public class BitFlipChunkReadStream(ChunkDataReadStream stream)
	: ChunkDataReadStream(stream.ChunkFileStream) {
	public override int Read(Span<byte> buffer) {
		var bytesRead = base.Read(buffer);

		FlipBits(buffer.Slice(0, bytesRead));
		return bytesRead;
	}

	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) {
		var bytesRead = await base.ReadAsync(buffer, token);

		FlipBits(buffer.Span.Slice(0, bytesRead));
		return bytesRead;
	}

	private static void FlipBits(Span<byte> buffer) {
		for (int i = 0; i < buffer.Length; i++)
			buffer[i] ^= 0xFF;
	}
}
