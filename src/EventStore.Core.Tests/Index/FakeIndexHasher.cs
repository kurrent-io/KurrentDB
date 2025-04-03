// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Index.Hashes;
using System;

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
