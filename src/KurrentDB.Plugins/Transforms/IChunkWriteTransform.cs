// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Plugins.Transforms;

public interface IChunkWriteTransform {
	ChunkDataWriteStream TransformData(ChunkDataWriteStream stream);
	ValueTask CompleteData(int footerSize, int alignmentSize, CancellationToken token = default);
	ValueTask<int> WriteFooter(ReadOnlyMemory<byte> footer, CancellationToken token = default);
}
