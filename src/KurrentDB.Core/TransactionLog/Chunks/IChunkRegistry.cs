// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.TransactionLog.Chunks.TFChunk;

namespace KurrentDB.Core.TransactionLog.Chunks;

public interface IChunkRegistry<out TChunk>
	where TChunk : class, IChunkBlob {

	// unsafe means that the returned chunk might not be initialized
	TChunk UnsafeTryGetChunkFor(long logPosition);

	TChunk UnsafeGetChunk(int chunkNum);
}
