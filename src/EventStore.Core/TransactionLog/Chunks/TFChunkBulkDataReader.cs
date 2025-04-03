// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.TransactionLog.Chunks.TFChunk;

namespace EventStore.Core.TransactionLog.Chunks;

public sealed class TFChunkBulkDataReader(TFChunk.TFChunk chunk, Stream streamToUse, bool isMemory)
	: TFChunkBulkReader(chunk, streamToUse, isMemory) {

	public override void SetPosition(long dataPosition) {
		var rawPos = dataPosition + ChunkHeader.Size;
		Stream.Position = rawPos;
	}

	public override async ValueTask<BulkReadResult> ReadNextBytes(Memory<byte> buffer, CancellationToken token) {
		if (Stream.Position is 0)
			Stream.Position = ChunkHeader.Size;

		var oldPos = (int)Stream.Position - ChunkHeader.Size;
		var toRead = Math.Min(Chunk.PhysicalDataSize - oldPos, buffer.Length);
		Debug.Assert(toRead >= 0);
		Stream.Position = Stream.Position; // flush read buffer
		int bytesRead = await Stream.ReadAsync(buffer.Slice(0, toRead), token);
		return new(oldPos,
			bytesRead,
			isEof: Chunk.IsReadOnly && oldPos + bytesRead == Chunk.PhysicalDataSize);
	}
}
