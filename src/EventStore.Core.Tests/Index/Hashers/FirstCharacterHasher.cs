// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Index.Hashes;

namespace EventStore.Core.Tests.Index.Hashers;

public class FirstCharacterHasher : ILongHasher<string> {
	public ulong Hash(string x) =>
		(ulong)(x.Length == 0 ? 0 : x[0].GetHashCode());
}
