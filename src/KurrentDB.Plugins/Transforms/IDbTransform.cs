// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Plugins.Transforms;

public interface IDbTransform {
	string Name { get; }
	TransformType Type { get; }
	IChunkTransformFactory ChunkFactory { get; }
}
