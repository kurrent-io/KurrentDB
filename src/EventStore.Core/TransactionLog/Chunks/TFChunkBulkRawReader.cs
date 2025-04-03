// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Common.Utils;
using EventStore.Core.TransactionLog.Chunks.TFChunk;

namespace EventStore.Core.TransactionLog.Chunks;

public sealed class TFChunkBulkRawReader(TFChunk.TFChunk chunk, Stream streamToUse, bool isMemory)
	: TFChunkBulkReader(chunk, streamToUse, isMemory), IChunkRawReader {

	public override void SetPosition(long rawPosition) {
		if (rawPosition >= Stream.Length)
			throw new ArgumentOutOfRangeException(nameof(rawPosition),
				$"Raw position {rawPosition} is out of bounds.");
		Stream.Position = rawPosition;
	}

	Stream IChunkRawReader.Stream => Stream;

	public override async ValueTask<BulkReadResult> ReadNextBytes(Memory<byte> buffer, CancellationToken token) {
		var oldPos = (int)Stream.Position;
		int bytesRead = await Stream.ReadAsync(buffer, token);
		return new(oldPos, bytesRead, isEof: Stream.Length == Stream.Position);
	}
}
