// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Transforms;

namespace EventStore.Core.Transforms;

public interface IGetChunkTransformFactory {
	IChunkTransformFactory ForNewChunk();
	IChunkTransformFactory ForExistingChunk(TransformType type);
}
