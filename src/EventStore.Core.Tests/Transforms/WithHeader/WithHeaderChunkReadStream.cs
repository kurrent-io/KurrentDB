// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using System;
using System.IO;
using EventStore.Plugins.Transforms;

namespace EventStore.Core.Tests.Transforms.WithHeader;

public class WithHeaderChunkReadStream(ChunkDataReadStream stream, int transformHeaderSize)
	: ChunkDataReadStream(stream.ChunkFileStream) {

	public override long Seek(long offset, SeekOrigin origin) {
		if (origin != SeekOrigin.Begin)
			throw new NotSupportedException();
		Position = offset;
		return offset;
	}

	public override long Position {
		get => ChunkFileStream.Position - transformHeaderSize;
		set => ChunkFileStream.Position = value + transformHeaderSize;
	}
}
