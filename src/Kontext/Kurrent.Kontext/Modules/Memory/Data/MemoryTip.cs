// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Memory.Data;

/// <summary>
/// Where one memory stands in its supersession chain. Absence from a lookup means no such memory
/// exists, which is what lets a caller tell a bad id apart from a stale one.
/// </summary>
public readonly record struct MemoryTip {
    /// <summary>The successor's id, or null while this memory is still the live tip.</summary>
    public required string? SupersededBy { get; init; }

    public bool IsLive => SupersededBy is null;
}
