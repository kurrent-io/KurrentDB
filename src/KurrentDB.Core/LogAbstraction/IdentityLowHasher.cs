// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Index.Hashes;
using StreamId = System.UInt32;

namespace KurrentDB.Core.LogAbstraction;

public class IdentityLowHasher : IHasher<StreamId> {
	public uint Hash(StreamId s) => s;
}
