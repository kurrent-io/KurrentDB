// Copyright (c) Event Store Ltd and/or licensed to Event Store Ltd under one or more agreements.
// Event Store Ltd licenses this file to you under the Event Store License v2 (see LICENSE.md).

using EventStore.Core.Index.Hashes;

namespace EventStore.Core.Tests.Index;

public class FakeIndexHasher : IHasher<string> {
	public uint Hash(byte[] data) {
		return (uint)data.Length;
	}

	public uint Hash(string s) {
		return uint.Parse(s);
	}

	public uint Hash(byte[] data, int offset, uint len, uint seed) {
		return (uint)data.Length;
	}
}
