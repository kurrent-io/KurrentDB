// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Index.Hashes;

namespace EventStore.Core.LogAbstraction;

// if the streams are only 32 bit, neednt have 64 bits in the index
public class IdentityHighHasher : IHasher<uint> {
	public uint Hash(uint s) => 0;
}
