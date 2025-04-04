// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System;

namespace EventStore.LogV3;

public struct ReadOnlySlicedRecord {
	public ReadOnlyMemory<byte> Bytes { get; init; }
	public ReadOnlyMemory<byte> HeaderMemory { get; init; }
	public ReadOnlyMemory<byte> SubHeaderMemory { get; init; }
	public ReadOnlyMemory<byte> PayloadMemory { get; init; }
}
