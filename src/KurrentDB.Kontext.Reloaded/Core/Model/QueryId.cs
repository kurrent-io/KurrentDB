// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Core;

/// <summary>
/// The identity of a recall/reflect call — a value object over a <see cref="Guid"/>, kept distinct from
/// <see cref="MemoryId"/> so the two id kinds can't be interchanged. Optional at call time (null ⇒ the
/// server assigns one); echoed back on the result.
/// </summary>
/// <param name="Value">The underlying identifier.</param>
public readonly record struct QueryId(Guid Value) {
	/// <summary>Mint a fresh, time-ordered id (UUIDv7) — sortable by creation time for index/stream locality.</summary>
	public static QueryId New => new(Guid.CreateVersion7());

	/// <summary>Parse the canonical string form carried on the wire.</summary>
	public static QueryId Parse(string value) => new(Guid.Parse(value));

	/// <inheritdoc/>
	public override string ToString() => Value.ToString();
}
