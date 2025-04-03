// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Transforms;

namespace EventStore.Core.Transforms.Identity;

public sealed class IdentityDbTransform : IDbTransform {
	public string Name => "identity";
	public TransformType Type => TransformType.Identity;
	public IChunkTransformFactory ChunkFactory { get; } = new IdentityChunkTransformFactory();
}
