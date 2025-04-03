// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;
using EventStore.Core.Index.Hashes;

namespace EventStore.Core.Tests.Services.Storage;

public class ByLengthHasher : IHasher<string> {
	public uint Hash(string s) {
		return (uint)s.Length;
	}

	public uint Hash(byte[] data) {
		return (uint)data.Length;
	}

	public uint Hash(byte[] data, int offset, uint len, uint seed) {
		return len;
	}
}
