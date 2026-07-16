// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Core;

/// <summary>Result of a retain — one <see cref="RetainedMemory"/> per input memory, in the same order.</summary>
public sealed record RetainResult {
	/// <summary>One result per input memory, in order.</summary>
	public required IReadOnlyList<RetainedMemory> Results { get; init; }
}

/// <summary>The outcome for a single stored memory.</summary>
public sealed record RetainedMemory {
	/// <summary>The stored memory's id. Retain always stores; <see cref="Related"/> never blocks it.</summary>
	public required MemoryId MemoryId { get; init; }

	/// <summary>
	/// Advisory, populated only when retain was asked to reconcile: existing memories this one looks to
	/// duplicate or contradict, nearest first.
	/// </summary>
	public IReadOnlyList<RelatedMemory> Related { get; init; } = [];
}

/// <summary>A pre-existing memory that looks related to a just-stored one.</summary>
/// <param name="MemoryId">The related memory.</param>
/// <param name="Similarity">Embedding cosine, ~0..1 (higher = more alike).</param>
public readonly record struct RelatedMemory(MemoryId MemoryId, double Similarity);
