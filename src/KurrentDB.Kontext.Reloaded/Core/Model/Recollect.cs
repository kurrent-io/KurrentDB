// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Core;

/// <summary>The dimension <see cref="IKontextMemory.RecollectAsync"/> orders its results by (no relevance).</summary>
public enum RecollectSort {
	/// <summary>Not set — defaults to <see cref="RetainedAt"/>.</summary>
	Unspecified = 0,

	/// <summary>When the memory was recorded — chronological. The default.</summary>
	RetainedAt = 1,

	/// <summary>The recency clock — surfaces what has been salient (recalled) lately.</summary>
	LastAccessedAt = 2,

	/// <summary>The agent's salience rating — surfaces the significant ones.</summary>
	Importance = 3,
}

/// <summary>Sort direction for a recollect listing.</summary>
public enum SortDirection {
	/// <summary>Not set — defaults to <see cref="Descending"/>.</summary>
	Unspecified = 0,

	/// <summary>High → low: newest / most-important / most-recently-accessed first.</summary>
	Descending = 1,

	/// <summary>Low → high: oldest / least first.</summary>
	Ascending = 2,
}

/// <summary>
/// The filters and ordering for <see cref="IKontextMemory.RecollectAsync"/> — browse by structure
/// (type/tags), not relevance. Recollect has no payload, so every input lives here.
/// </summary>
public sealed record RecollectOptions {
	/// <summary>Any-of these types (empty ⇒ any type).</summary>
	public IReadOnlyList<MemoryType> Types { get; init; } = [];

	/// <summary>Only memories carrying ALL of these tags (empty ⇒ no tag filter).</summary>
	public IReadOnlyList<Tag> Tags { get; init; } = [];

	/// <summary>Include retracted memories. Default false — retracted stay hidden unless explicitly asked for.</summary>
	public bool IncludeRetracted { get; init; }

	/// <summary>Max memories to return. With a limit, <see cref="Sort"/> decides which slice.</summary>
	public int Limit { get; init; }

	/// <summary>How to order results. Default: newest-retained first.</summary>
	public RecollectSort Sort { get; init; }

	/// <summary>Sort direction. Default: descending.</summary>
	public SortDirection Direction { get; init; }
}
