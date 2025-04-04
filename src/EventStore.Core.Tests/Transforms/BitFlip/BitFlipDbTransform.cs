// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Plugins.Transforms;

namespace EventStore.Core.Tests.Transforms.BitFlip;

public class BitFlipDbTransform : IDbTransform {
	public string Name => "bitflip";
	public TransformType Type => (TransformType)0xFF;
	public IChunkTransformFactory ChunkFactory { get; } = new BitFlipChunkTransformFactory();
}
