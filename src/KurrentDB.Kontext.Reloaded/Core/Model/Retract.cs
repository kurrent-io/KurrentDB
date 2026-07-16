// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Core;

/// <summary>Result of a retract — the memory plus every derived memory removed by the cascade.</summary>
public sealed record RetractResult {
	/// <summary>The retracted memory plus every derived memory removed by the cascade.</summary>
	public required IReadOnlyList<MemoryId> RetractedMemoryIds { get; init; }
}
