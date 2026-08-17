// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

namespace Kurrent.Kontext.Modules.Entities.Extraction;

/// <summary>
/// How the pipeline folds several stages' occurrences into one result — the extraction-side
/// mirror of retrieval's <c>ICandidateFuser</c>. Every merger deduplicates on
/// <see cref="ExtractedEntity.Key"/> — normalized name plus type — so entities of different
/// types never merge.
/// </summary>
public interface IEntityMerger {
	/// <summary>
	/// True when the stage walk should stop at the first stage that satisfied the success
	/// threshold — the fold only ever sees that one result, so running later stages buys nothing.
	/// </summary>
	bool StopsOnSuccess => false;

	ExtractionResult Merge(IReadOnlyList<ExtractionResult> results);
}

/// <summary>Keep every unique entity; on a key collision the higher confidence wins.</summary>
public sealed class UnionMerger : IEntityMerger {
	public ExtractionResult Merge(IReadOnlyList<ExtractionResult> results) {
		var best = new Dictionary<string, ExtractedEntity>();

		foreach (var entity in results.SelectMany(result => result.Entities))
			if (!best.TryGetValue(entity.Key, out var seen) || entity.Confidence > seen.Confidence)
				best[entity.Key] = entity;

		return new() { Entities = [.. best.Values] };
	}
}

/// <summary>
/// Keep only entities that more than one stage found (consensus), at the highest confidence
/// seen, boosted 10% (capped at 1.0) for the agreement.
/// </summary>
public sealed class IntersectionMerger : IEntityMerger {
	public ExtractionResult Merge(IReadOnlyList<ExtractionResult> results) {
		var occurrences = new Dictionary<string, List<ExtractedEntity>>();

		// Count agreement per key across STAGES, not within one stage's repeats.
		foreach (var result in results)
			foreach (var entity in result.Entities.DistinctBy(entity => entity.Key)) {
				if (!occurrences.TryGetValue(entity.Key, out var list))
					occurrences[entity.Key] = list = [];

				list.Add(entity);
			}

		var agreed = occurrences.Values
			.Where(list => list.Count > 1)
			.Select(list => {
				var top = list.MaxBy(entity => entity.Confidence)!;
				return top with { Confidence = Math.Min(1.0, top.Confidence * 1.1) };
			});

		return new() { Entities = [.. agreed] };
	}
}

/// <summary>The first stage's entities stand; later stages only fill keys the earlier ones missed.</summary>
public sealed class CascadeMerger : IEntityMerger {
	public ExtractionResult Merge(IReadOnlyList<ExtractionResult> results) {
		var merged = new Dictionary<string, ExtractedEntity>();

		foreach (var entity in results.SelectMany(result => result.Entities))
			merged.TryAdd(entity.Key, entity);

		return new() { Entities = [.. merged.Values] };
	}
}

/// <summary>Take the first stage that produced enough entities and skip the rest.</summary>
public sealed class FirstSuccessMerger : IEntityMerger {
	public bool StopsOnSuccess => true;

	public ExtractionResult Merge(IReadOnlyList<ExtractionResult> results) =>
		results.FirstOrDefault(result => result.Entities.Count > 0) ?? ExtractionResult.Empty;
}
