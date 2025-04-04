// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Transforms;

namespace EventStore.Core.Tests.Transforms.BitFlip;

public class BitFlipChunkReadTransform : IChunkReadTransform {
	public ChunkDataReadStream TransformData(ChunkDataReadStream dataStream) =>
		new BitFlipChunkReadStream(dataStream);
}
