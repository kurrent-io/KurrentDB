// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using EventStore.Core.Index.Hashes;

namespace EventStore.Core.Tests.Index.Hashers;

public class ConstantHasher : IHasher<string> {
	private readonly uint _const;
	public ConstantHasher(uint @const) {
		_const = @const;
	}

	public uint Hash(string s) {
		return _const;
	}
}
