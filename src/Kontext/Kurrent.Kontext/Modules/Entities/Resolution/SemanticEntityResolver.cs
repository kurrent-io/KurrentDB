// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Numerics.Tensors;
using Kurrent.Kontext.Modules.Entities.Data;

namespace Kurrent.Kontext.Modules.Entities.Resolution;

/// <summary>
/// Embedding resolution: the stored entity of the probe's type whose name embedding is most
/// cosine-similar to the probe's, at or above the threshold. Catches what strings cannot —
/// "IBM" and "International Business Machines" — and therefore runs LAST in the composite
/// chain, where only genuinely hard cases reach it.
/// <para>A probe without an embedding resolves to no match: the resolver never calls a model —
/// the projector owns embedding, batched.</para>
/// </summary>
public sealed class SemanticEntityResolver(
	KontextEntityStore store,
	double threshold = SemanticEntityResolver.DefaultThreshold,
	int maxCandidates = SemanticEntityResolver.DefaultMaxCandidates
) : IEntityResolver {
	// The reference pipeline's defaults: 0.8 cosine floor, 10 nearest candidates considered.
	public const double DefaultThreshold     = 0.8;
	public const int    DefaultMaxCandidates = 10;

	public async ValueTask<EntityResolution> ResolveAsync(ResolutionProbe probe, CancellationToken ct = default) {
		if (probe.Probe.Embedding is not { Length: > 0 } embedding)
			return EntityResolution.Unmatched;

		var hits = await store.SearchSimilarAsync(embedding, probe.Probe.EntityType, maxCandidates, ct).ConfigureAwait(false);

		var best = hits
			.Where(hit => hit.CosineSimilarity >= threshold)
			.MaxBy(hit => hit.CosineSimilarity);

		EntityRow? match = best?.Entity;
		var        score = best?.CosineSimilarity ?? 0.0;

		// The batch pool scores in process — pending entities are not in the store's index yet.
		// A pending entity minted without an embedding (a stored row merely touched this batch)
		// cannot be scored and is skipped; its stored embedding already competed above.
		foreach (var pending in probe.PendingOfType()) {
			if (pending.Embedding.Length != embedding.Length)
				continue;

			var similarity = Math.Clamp(TensorPrimitives.CosineSimilarity(embedding, pending.Embedding), 0f, 1f);

			if (similarity >= threshold && similarity > score) {
				match = pending.Row;
				score = similarity;
			}
		}

		return match is null
			? EntityResolution.Unmatched
			: new() { Match = match, Score = score, Method = ResolutionMethod.Semantic };
	}
}
