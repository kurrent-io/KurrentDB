// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Transforms;

namespace EventStore.Core.Tests.Transforms.WithHeader;
public class WithHeaderDbTransform : IDbTransform {
	public string Name => "withheader";
	public TransformType Type => (TransformType) 0xFD;
	public IChunkTransformFactory ChunkFactory { get; } = new WithHeaderChunkTransformFactory();
}
