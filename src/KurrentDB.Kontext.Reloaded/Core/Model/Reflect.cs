// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Core;

/// <summary>
/// The optional knobs for <see cref="IKontextMemory.ReflectAsync"/> — the theme (query) is a direct parameter.
/// </summary>
public sealed record ReflectOptions {
	/// <summary>Optional correlation id; null ⇒ the server assigns one.</summary>
	public QueryId? QueryId { get; init; }

	/// <summary>Scope the reflection to memories carrying these tags; empty ⇒ range over all.</summary>
	public IReadOnlyList<Tag> Tags { get; init; } = [];
}

/// <summary>Result of a reflect pass: the ids it synthesized, superseded, and retracted.</summary>
public sealed record ReflectResult {
	/// <summary>Correlates to the reflect call — supplied via <see cref="ReflectOptions.QueryId"/>, or server-assigned.</summary>
	public required QueryId QueryId { get; init; }

	/// <summary>Ids of memories synthesized.</summary>
	public IReadOnlyList<MemoryId> SynthesizedMemoryIds { get; init; } = [];

	/// <summary>Ids of memories superseded.</summary>
	public IReadOnlyList<MemoryId> SupersededMemoryIds { get; init; } = [];

	/// <summary>Ids of memories retracted.</summary>
	public IReadOnlyList<MemoryId> RetractedMemoryIds { get; init; } = [];
}
