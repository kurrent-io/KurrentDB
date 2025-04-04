// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Transforms;

namespace EventStore.Core.Tests.Transforms.WithHeader;
public class WithHeaderChunkTransform(int transformHeaderSize) : IChunkTransform {
	public IChunkReadTransform Read { get; } = new WithHeaderChunkReadTransform(transformHeaderSize);
	public IChunkWriteTransform Write { get; } = new WithHeaderChunkWriteTransform(transformHeaderSize);
}
