// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Modules.Entities.Data;
using Kurrent.Kontext.Modules.Entities.Extraction;
using Kurrent.Kontext.Retrieval;

namespace Kurrent.Kontext.Modules.Entities;

/// <summary>
/// The entities read model behind retrieval's <see cref="IEntityIndex"/> port, mirroring how
/// <see cref="Kurrent.Kontext.Data.KontextDataStore"/> serves <see cref="IMemoryIndex"/>: the
/// pipeline states what it needs, the read model answers, and neither knows the other's internals.
/// <para>Recognition is deliberately free. The question is normalized with the SAME
/// <see cref="EntityName"/> rule the extractors and the writer file names under — so a stored name
/// is findable by the exact key it was stored as — and every contiguous word run up to
/// <see cref="MaxSurfaceWords"/> words is looked up in one query. No model, no embedding, no fuzzy
/// scoring at question time; that is resolution's job, on its own clock.</para>
/// <para>Quiet before the read model exists: the entity projector bootstraps its own schema when
/// the node goes operational, so a retrieval that beats it finds nothing instead of failing. The
/// probe latches on first success — a racing pair of readers costs one extra catalog lookup, which
/// is cheaper than a lock on the read path.</para>
/// </summary>
public sealed class KontextEntityIndex(KontextEntityStore store) : IEntityIndex {
	/// <summary>The one link status the read path crosses: a doubt still awaiting a verdict.</summary>
	const string PendingStatus = "pending";

	/// <summary>Longest stored name the lookup can find, in words ("countess of lovelace" is three).</summary>
	const int MaxSurfaceWords = 4;

	// A read-time rail, not a tuning knob: n-grams grow with the question, and a ±10% tiebreaker
	// must never turn a pasted wall of text into the most expensive read in the pipeline.
	const int MaxQuestionWords = 64;

	bool _projected;

	public async ValueTask<IReadOnlyList<EntityMatch>> MatchAsync(string question, CancellationToken ct = default) {
		var surfaces = Surfaces(question);

		if (surfaces.Count == 0 || !await ProjectedAsync(ct).ConfigureAwait(false))
			return [];

		var matches = await store.MatchBySurfacesAsync(surfaces, ct).ConfigureAwait(false);

		return [.. matches.Select(entity => new EntityMatch(entity.EntityId, entity.MentionCount))];
	}

	public async ValueTask<IReadOnlyList<EntityNote>> ListNotesAsync(IReadOnlyCollection<string> entityIds, CancellationToken ct = default) {
		if (entityIds.Count == 0 || !await ProjectedAsync(ct).ConfigureAwait(false))
			return [];

		var links = await store.ListLinksTouchingAsync(entityIds, PendingStatus, ct).ConfigureAwait(false);

		return [.. links.Select(link => new EntityNote(link.SourceEntityId, link.TargetEntityId, link.Confidence))];
	}

	public async ValueTask<IReadOnlyList<EntityMention>> ListMentionsAsync(IReadOnlyCollection<string> entityIds, CancellationToken ct = default) {
		if (entityIds.Count == 0 || !await ProjectedAsync(ct).ConfigureAwait(false))
			return [];

		var mentions = await store.ListMentionsOfEntitiesAsync(entityIds, ct).ConfigureAwait(false);

		return [.. mentions.Select(mention => new EntityMention(mention.EntityId, mention.MemoryId))];
	}

	async ValueTask<bool> ProjectedAsync(CancellationToken ct) =>
		_projected || (_projected = await store.ExistsAsync(ct).ConfigureAwait(false));

	/// <summary>
	/// Every contiguous word run of the normalized question, up to <see cref="MaxSurfaceWords"/>
	/// long and deduplicated: a stored name is one of those runs, or it is not in the question at
	/// all. <see cref="EntityName.IsValid"/> drops the runs no name can be (stopwords, numbers),
	/// which is what keeps "is", "the", and "2026" from matching anything.
	/// </summary>
	static List<string> Surfaces(string question) {
		var words = EntityName.Normalize(question)
			.Split(' ', StringSplitOptions.RemoveEmptyEntries)
			.Take(MaxQuestionWords)
			.ToList();

		var seen     = new HashSet<string>(StringComparer.Ordinal);
		var surfaces = new List<string>();

		for (var start = 0; start < words.Count; start++)
		for (var length = 1; length <= MaxSurfaceWords && start + length <= words.Count; length++) {
			var surface = string.Join(' ', words.GetRange(start, length));

			if (EntityName.IsValid(surface) && seen.Add(surface))
				surfaces.Add(surface);
		}

		return surfaces;
	}
}
