// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace EventStore.Core.Data;

public readonly record struct ChunkInfo {
	public required int ChunkStartNumber { get; init; }
	public required int ChunkEndNumber { get; init; }
	public required bool IsRemote { get; init; }
}
