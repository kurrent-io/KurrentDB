// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Core;

/// <summary>
/// The optional knobs for <see cref="IKontextMemory.RecallAsync"/> — the query itself is a direct parameter.
/// </summary>
public sealed record RecallOptions {
	/// <summary>Optional correlation id; null ⇒ the server assigns one.</summary>
	public QueryId? QueryId { get; init; }

	/// <summary>How many memories to return after ranking. A handful (5–10) is best; more dilutes the top.</summary>
	public int Limit { get; init; }

	/// <summary>Drop anything scored below this cutoff. 0 ⇒ see everything that ranked.</summary>
	public double MinScore { get; init; }

	/// <summary>Optional pre-filter: only memories carrying these tags are considered. Empty ⇒ search everything.</summary>
	public IReadOnlyList<Tag> Tags { get; init; } = [];

	/// <summary>When true, each hit carries the full <see cref="StoredMemory"/> instead of the lean projection.</summary>
	public bool IncludeFull { get; init; }
}

/// <summary>Result of a recall — hits ranked best-first.</summary>
public sealed record RecallResult {
	/// <summary>The query's correlation id — supplied via <see cref="RecallOptions.QueryId"/>, or server-assigned.</summary>
	public required QueryId QueryId { get; init; }

	/// <summary>Ranked best-first.</summary>
	public required IReadOnlyList<RecalledMemory> Memories { get; init; }
}

/// <summary>
/// One recall hit — a ranking score plus the memory, lean by default or the full folded record when
/// <see cref="RecallOptions.IncludeFull"/> was set. Pattern-match the arm to read the projection.
/// </summary>
public abstract record RecalledMemory {
	// Private ctor closes the hierarchy to the two arms below (see Citation for the same pattern).
	RecalledMemory() { }

	/// <summary>Final ranking score; higher = stronger match. A low top score means nothing matched well.</summary>
	public required double Score { get; init; }

	/// <summary>The default lean projection — enough to use and trust a hit.</summary>
	public sealed record Lean : RecalledMemory {
		public required LeanMemory Memory { get; init; }
	}

	/// <summary>The complete folded record (as reclaim returns) — present when include-full was set.</summary>
	public sealed record Full : RecalledMemory {
		public required StoredMemory Memory { get; init; }
	}
}

/// <summary>
/// The lean recall projection: enough to use a memory and judge it, without the provenance trail (evidence,
/// reasoning). For those, request the full body or reclaim by id.
/// </summary>
public sealed record LeanMemory {
	/// <summary>Reclaim by this for the full record.</summary>
	public required MemoryId MemoryId { get; init; }

	/// <summary>The trust axis.</summary>
	public required MemoryType MemoryType { get; init; }

	/// <summary>The memory text.</summary>
	public required string Content { get; init; }

	/// <summary>Labels carried on the memory.</summary>
	public IReadOnlyList<Tag> Tags { get; init; } = [];

	/// <summary>Its salience level.</summary>
	public MemoryImportance Importance { get; init; }

	/// <summary>Age, for context.</summary>
	public required DateTimeOffset RetainedAt { get; init; }
}
