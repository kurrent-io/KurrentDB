// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using KurrentDB.Core.Index.Hashes;

namespace KurrentDB.Kontext.Indexing;

// same stream hash KurrentDB's index uses
public static class StreamHasher {
	static readonly IHasher<string> Low = new XXHashUnsafe();
	static readonly IHasher<string> High = new Murmur3AUnsafe();

	public static ulong Hash(string streamName) =>
		((ulong)Low.Hash(streamName) << 32) | High.Hash(streamName);
}