// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Transforms;

namespace KurrentDB.Core.Tests.Transforms.BitFlip;

public class BitFlipDbTransform : IDbTransform {
	public string Name => "bitflip";
	public TransformType Type => (TransformType)0xFF;
	public IChunkTransformFactory ChunkFactory { get; } = new BitFlipChunkTransformFactory();
}
