// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Plugins.Transforms;

namespace KurrentDB.Core.Transforms.Identity;

public sealed class IdentityChunkTransform : IChunkTransform {
	public IChunkReadTransform Read => IdentityChunkReadTransform.Instance;
	public IChunkWriteTransform Write { get; } = new IdentityChunkWriteTransform();
}
