// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Core;

/// <summary>
/// The complete, folded read model of a single memory — every lifecycle event applied. Self-contained, for
/// inspection and direct fetch (<see cref="IKontextMemory.ReclaimAsync"/> and
/// <see cref="IKontextMemory.RecollectAsync"/>), not for ranked retrieval. Deliberately separate from the
/// <see cref="Memory"/> command shape so read and write models evolve independently.
/// </summary>
public sealed record StoredMemory {
	/// <summary>Unique identifier of the memory.</summary>
	public required MemoryId MemoryId { get; init; }

	/// <summary>The record's cognitive type — the trust axis.</summary>
	public required MemoryType MemoryType { get; init; }

	/// <summary>The memory's textual content.</summary>
	public required string Content { get; init; }

	/// <summary>The salience level set at retain.</summary>
	public MemoryImportance Importance { get; init; }

	/// <summary>What this memory rests on — <see cref="Evidence.None"/> for a raw observation.</summary>
	public Evidence Evidence { get; init; } = Evidence.None;

	/// <summary>Scoped or bare labels.</summary>
	public IReadOnlyList<Tag> Tags { get; init; } = [];

	/// <summary>Emotional tone, set by the enrichment pipeline.</summary>
	public MemorySentiment Sentiment { get; init; }

	/// <summary>Time-pressure, set by the enrichment pipeline.</summary>
	public MemoryUrgency Urgency { get; init; }

	/// <summary>The world-time span the content refers to.</summary>
	public TemporalContext? Validity { get; init; }

	/// <summary>Memory ids this memory supersedes (replaces).</summary>
	public IReadOnlyList<MemoryId> Supersedes { get; init; } = [];

	/// <summary>When the memory was retained.</summary>
	public required DateTimeOffset RetainedAt { get; init; }

	/// <summary>The recency clock — advanced by each recall. Null if never accessed.</summary>
	public DateTimeOffset? LastAccessedAt { get; init; }

	/// <summary>When the memory was retracted, if it was.</summary>
	public DateTimeOffset? RetractedAt { get; init; }

	/// <summary>When a newer memory superseded this one, if any.</summary>
	public DateTimeOffset? SupersededAt { get; init; }

	/// <summary>The memory that superseded this one; set only when <see cref="SupersededAt"/> is set.</summary>
	public MemoryId? SupersededBy { get; init; }
}
