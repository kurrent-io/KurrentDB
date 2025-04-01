// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.Security.EncryptionAtRest;

public readonly record struct MasterKey {
	public ushort Id { get; }
	public ReadOnlyMemory<byte> Key { get; }

	public MasterKey(int id, ReadOnlyMemory<byte> key) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(id, ushort.MaxValue);

		Id = (ushort) id;
		Key = key;
	}
}
