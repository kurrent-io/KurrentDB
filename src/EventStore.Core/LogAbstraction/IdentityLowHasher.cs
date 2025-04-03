// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Index.Hashes;
using StreamId = System.UInt32;

namespace EventStore.Core.LogAbstraction;

public class IdentityLowHasher : IHasher<StreamId> {
	public uint Hash(StreamId s) => s;
}
