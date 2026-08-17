// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Modules.Entities.Data;

namespace Kurrent.Kontext.Modules.Entities.Resolution;

/// <summary>
/// Edit-distance resolution: scores the probe against every stored name AND alias of the
/// probe's type with token-sort indel similarity, and keeps the best hit at or above the
/// threshold. Catches typos and word-order variants ("Jon Smith", "Smith, John") that
/// equality misses and embeddings often over-match.
/// </summary>
public sealed class FuzzyEntityResolver(
	KontextEntityStore store,
	double threshold = FuzzyEntityResolver.DefaultThreshold,
	int maxCandidates = FuzzyEntityResolver.DefaultMaxCandidates
) : IEntityResolver {
	// The reference pipeline's composite defaults: 0.85 floor, scored in process over the
	// type's population. The candidate cap is a safety rail for pathological populations —
	// most-mentioned entities score first, which is also where duplicates live.
	public const double DefaultThreshold     = 0.85;
	public const int    DefaultMaxCandidates = 1000;

	public async ValueTask<EntityResolution> ResolveAsync(ResolutionProbe probe, CancellationToken ct = default) {
		var name       = probe.Probe.NormalizedName;
		var candidates = await store.ListByTypeAsync(probe.Probe.EntityType, maxCandidates, ct).ConfigureAwait(false);

		EntityRow? best      = null;
		var        bestScore = 0.0;

		// Store first, batch pool second — on a tied score the committed row wins.
		foreach (var candidate in candidates.Concat(probe.PendingOfType().Select(pending => pending.Row))) {
			var score = Score(name, candidate);

			if (score >= threshold && score > bestScore) {
				best      = candidate;
				bestScore = score;
			}
		}

		return best is null
			? EntityResolution.Unmatched
			: new() { Match = best, Score = bestScore, Method = ResolutionMethod.Fuzzy };
	}

	// An alias is a real name the entity goes by, so it competes equally with the canonical one.
	static double Score(string normalizedName, EntityRow candidate) {
		var score = NameSimilarity.TokenSortRatio(normalizedName, candidate.NormalizedName);

		foreach (var alias in candidate.Aliases)
			score = Math.Max(score, NameSimilarity.TokenSortRatio(normalizedName, alias));

		return score;
	}
}
