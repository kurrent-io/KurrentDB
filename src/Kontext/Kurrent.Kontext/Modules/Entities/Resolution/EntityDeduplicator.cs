// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Modules.Entities.Data;

namespace Kurrent.Kontext.Modules.Entities.Resolution;

/// <summary>What the write path must do with one extracted occurrence.</summary>
public enum DeduplicationAction {
	/// <summary>No stored entity is close enough — create a new one.</summary>
	Create,

	/// <summary>Confidently the same entity — fold into the match (alias, mention count, recency).</summary>
	Merge,

	/// <summary>
	/// Suspiciously close but not certain — create a new entity AND record a pending link to
	/// the match for review. Wrong merges destroy identity; wrong flags cost a review.
	/// </summary>
	Flag,
}

/// <summary>The dedup verdict: the action, and for Merge/Flag the entity it points at.</summary>
public sealed record DeduplicationDecision {
	public static readonly DeduplicationDecision CreateNew = new() { Action = DeduplicationAction.Create };

	public required DeduplicationAction Action { get; init; }

	public EntityRow?       Match  { get; init; }
	public double           Score  { get; init; }
	public ResolutionMethod Method { get; init; } = ResolutionMethod.None;
}

/// <summary>
/// The dedup policy's two lines in the sand. Mutable settings class — config binding does not
/// cope with records.
/// <para>Scale note: both thresholds grade RAW similarity — token-sort ratio for fuzzy matches
/// and clamped raw cosine for semantic ones. The reference implementation's identical-looking
/// numbers sat on Neo4j's vector-index scale, <c>(1+cos)/2</c>, where 0.95/0.85 mean raw cosine
/// 0.9/0.7 — so on the semantic leg these defaults are deliberately STRICTER than the reference
/// behaved. Tune here, not by rescaling scores.</para>
/// </summary>
public sealed class EntityDeduplicationOptions {
	/// <summary>At or above this similarity the match merges without questions. Exact matches score 1.0 and always clear it.</summary>
	public double AutoMergeThreshold { get; set; } = 0.95;

	/// <summary>At or above this (but below auto-merge) the pair is flagged for review instead of merged.</summary>
	public double FlagThreshold { get; set; } = 0.85;
}

/// <summary>
/// The reference pipeline's dedup decision, ported: resolve, then grade the best match against
/// two thresholds — ≥ auto-merge folds, ≥ flag creates-and-links, anything weaker creates
/// cleanly. The resolver supplies the similarity; this class only draws the lines.
/// </summary>
public sealed class EntityDeduplicator {
	readonly IEntityResolver            _resolver;
	readonly EntityDeduplicationOptions _options;

	public EntityDeduplicator(IEntityResolver resolver, EntityDeduplicationOptions? options = null) {
		options ??= new();

		if (options.AutoMergeThreshold is <= 0 or > 1 || options.FlagThreshold is <= 0 or > 1)
			throw new ArgumentException("Deduplication thresholds must be in (0, 1].", nameof(options));

		if (options.FlagThreshold > options.AutoMergeThreshold)
			throw new ArgumentException("FlagThreshold must not exceed AutoMergeThreshold — the flag band sits below auto-merge.", nameof(options));

		_resolver = resolver;
		_options  = options;
	}

	public ValueTask<DeduplicationDecision> DecideAsync(EntityProbe probe, CancellationToken ct = default) =>
		DecideAsync(ResolutionProbe.Of(probe), ct);

	public async ValueTask<DeduplicationDecision> DecideAsync(ResolutionProbe probe, CancellationToken ct = default) {
		var resolution = await _resolver.ResolveAsync(probe, ct).ConfigureAwait(false);

		if (!resolution.IsMatch)
			return DeduplicationDecision.CreateNew;

		if (resolution.Score >= _options.AutoMergeThreshold)
			return Decision(DeduplicationAction.Merge, resolution);

		if (resolution.Score >= _options.FlagThreshold)
			return Decision(DeduplicationAction.Flag, resolution);

		return DeduplicationDecision.CreateNew;

		static DeduplicationDecision Decision(DeduplicationAction action, EntityResolution resolution) => new() {
			Action = action,
			Match  = resolution.Match,
			Score  = resolution.Score,
			Method = resolution.Method,
		};
	}
}
