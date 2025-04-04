// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.TransactionLog.Chunks.TFChunk;

// This is intended to be used within IChunkFileSystem only, to avoid leaking details of which
// chunks are remote and which are local.
public interface ILocatorCodec {
	string EncodeLocal(string fileName);
	string EncodeRemote(int chunkNumber);

	// returns false => fileName is set (local)
	// returns true => chunkNumber is set (remote)
	bool Decode(string locator, out int chunkNumber, out string fileName);
}
