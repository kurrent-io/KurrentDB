// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Core;

/// <summary>
/// A memory's intrinsic content — the write/command shape passed to <see cref="IKontextMemory.RetainAsync"/>.
/// The read path returns the folded <see cref="StoredMemory"/>; the two shapes are deliberately separate so
/// the write and read models evolve independently.
/// </summary>
public sealed record Memory {
	/// <summary>Optional. When null, the server assigns a new id.</summary>
	public MemoryId? Id { get; init; }

	/// <summary>The record's cognitive type — the trust axis.</summary>
	public required MemoryType Type { get; init; } = MemoryType.Observation;

	/// <summary>The memory's textual content.</summary>
	public required string Content { get; init; }

	/// <summary>Salience rating; higher keeps it retrievable longer as recency fades.</summary>
	public MemoryImportance Importance { get; init; } = MemoryImportance.Normal;

	/// <summary>What this memory rests on — <see cref="Evidence.None"/> for a raw observation.</summary>
	public Evidence Evidence { get; init; } = Evidence.None;

	/// <summary>Scoped or bare labels.</summary>
	public IReadOnlyList<Tag> Tags { get; init; } = [];

	/// <summary>Emotional tone — set by the enrichment pipeline, not the agent.</summary>
	public MemorySentiment Sentiment { get; init; } = MemorySentiment.Neutral;

	/// <summary>Time-pressure — set by the enrichment pipeline, not the agent.</summary>
	public MemoryUrgency Urgency { get; init; } = MemoryUrgency.Medium;

	/// <summary>The world-time span the content refers to. Null when the text implies none.</summary>
	public TemporalContext? Validity { get; init; }

	/// <summary>
	/// Memory ids this memory supersedes (replaces). The superseded ones stay readable, marked superseded.
	/// Set at retain — there is no separate supersede operation.
	/// </summary>
	public IReadOnlyList<MemoryId> Supersedes { get; init; } = [];
}
