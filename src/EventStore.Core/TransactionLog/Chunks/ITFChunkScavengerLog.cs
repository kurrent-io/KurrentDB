// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Index;

namespace EventStore.Core.TransactionLog.Chunks;

public interface ITFChunkScavengerLog : IIndexScavengerLog {
	string ScavengeId { get; }

	long SpaceSaved { get; }

	void ScavengeStarted();

	void ScavengeStarted(bool alwaysKeepScavenged, bool mergeChunks, int startFromChunk, int threads);

	void ChunksScavenged(int chunkStartNumber, int chunkEndNumber, TimeSpan elapsed, long spaceSaved);

	void ChunksNotScavenged(int chunkStartNumber, int chunkEndNumber, TimeSpan elapsed, string errorMessage);

	void ChunksMerged(int chunkStartNumber, int chunkEndNumber, TimeSpan elapsed, long spaceSaved);

	void ChunksNotMerged(int chunkStartNumber, int chunkEndNumber, TimeSpan elapsed, string errorMessage);

	void ScavengeCompleted(ScavengeResult result, string error, TimeSpan elapsed);
}

public enum ScavengeResult {
	Success,
	Stopped,
	Errored,
	Interrupted,
}

public enum LastScavengeResult {
	Unknown,
	InProgress,
	Success,
	Stopped,
	Errored,
}
