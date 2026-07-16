// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Core;

/// <summary>
/// The identity of a memory — a value object over a <see cref="Guid"/> so it can't be silently swapped with
/// another identity (a <see cref="QueryId"/>, a raw string). Conversions are explicit on purpose: keep the
/// wrapper through the domain and unwrap only at the edge, where it maps to/from the wire.
/// </summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct MemoryId(Guid Value) {
	/// <summary>Mint a fresh, time-ordered id (UUIDv7) — sortable by creation time for index/stream locality.</summary>
	public static MemoryId New => new(Guid.CreateVersion7());

	/// <summary>Parse the canonical string form carried on the wire.</summary>
	public static MemoryId Parse(string value) => new(Guid.Parse(value));

	/// <inheritdoc/>
	public override string ToString() => Value.ToString();
	
	public static implicit operator string(MemoryId id) => id.Value.ToString();
}
