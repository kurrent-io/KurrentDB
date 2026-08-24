// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Contracts.V3.Entities;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Entities.Extraction;
using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Entities;

/// <summary>
/// Resolves names in text to catalog entities through a cascade of tiers, cheapest first. Each
/// tier claims the names it is confident about and passes the rest down; whatever survives every
/// tier becomes a new entity. One tier per partial file.
/// </summary>
public sealed partial class KontextEntityResolver(
    KontextDataSource dts,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    EntityResolverOptions? options = null,
    IEntityDisambiguator? disambiguator = null
) {
	readonly EntityResolverOptions _options = options ?? new EntityResolverOptions();

	/// <summary>Names created here, so later mentions link instead of re-creating.</summary>
    readonly Dictionary<EntityKey, string> _created = [];

    /// <summary>Created names by folded shape, so "pottery classes" links to "pottery class".</summary>
    readonly Dictionary<EntityKey, string> _createdFolded = [];

    public async ValueTask<IReadOnlyDictionary<EntityKey, ResolvedEntity>> ResolveAsync(
        IEnumerable<ExtractedEntity> entities, CancellationToken ct = default
    ) {
        var batch = entities as IReadOnlyCollection<ExtractedEntity> ?? [.. entities];
        var pass  = await BeginPassAsync(batch, ct).ConfigureAwait(false);

        await ClaimExactAsync(pass, ct).ConfigureAwait(false);
        await ClaimLexicalAsync(pass, ct).ConfigureAwait(false);
        await ClaimSemanticAsync(pass, ct).ConfigureAwait(false);
        await ClaimDisambiguatedAsync(pass, ct).ConfigureAwait(false);

        CreateUnresolved(pass);

        return pass.Resolutions;
    }

    /// <summary>Folds the batch once and claims names this resolver already created.</summary>
    async ValueTask<ResolutionPass> BeginPassAsync(IReadOnlyCollection<ExtractedEntity> batch, CancellationToken ct) {
        var folded = _options.LexicalTier
            ? await FoldAsync([.. batch.Select(entity => entity.Text)], ct).ConfigureAwait(false)
            : new Dictionary<string, string>();

        var pass = new ResolutionPass(folded, Judged: disambiguator is not null && _options.LlmTier);

        foreach (var entity in batch) {
            var key = EntityKey.For(entity.EntityType, entity.Text);

            if (_created.TryGetValue(key, out var createdId))
                pass.Resolutions.TryAdd(key, new ResolvedEntity(createdId, 1.0, ResolutionMethod.Exact));
            else if (_options.LexicalTier && _createdFolded.TryGetValue(FoldedKey(key, folded[entity.Text]), out var foldedId))
                pass.Resolutions.TryAdd(key, new ResolvedEntity(foldedId, StemMatchConfidence, ResolutionMethod.FullText));
            else
                pass.Pending.TryAdd(key, entity.Text);
        }

        return pass;
    }

    /// <summary>Turns every name no tier claimed into a new entity with a deterministic id.</summary>
    void CreateUnresolved(ResolutionPass pass) {
        foreach (var (key, text) in pass.Unresolved) {
            var created = EntityId.For(key.EntityType, text);

            _created[key] = created;

            if (_options.LexicalTier)
                _createdFolded.TryAdd(FoldedKey(key, pass.Folded[text]), created);

            pass.Claim(key, new ResolvedEntity(created, 1.0, ResolutionMethod.Created));
        }
    }

    static EntityKey FoldedKey(EntityKey key, string foldedText) => new(key.EntityType, foldedText);

    static void Remember(
        Dictionary<EntityKey, List<EntityCandidate>> candidates, EntityKey key, EntityCandidate candidate
    ) {
        var forKey = candidates.TryGetValue(key, out var existing) ? existing : candidates[key] = [];

        if (forKey.All(known => known.EntityId != candidate.EntityId))
            forKey.Add(candidate);
    }

    /// <summary>
    /// One batch moving through the cascade. Names start in <see cref="Pending"/>, tiers claim
    /// them into <see cref="Resolutions"/>, and <see cref="Candidates"/> collects the entities a
    /// tier surfaced but refused to merge, for the disambiguation tier to choose from.
    /// </summary>
    sealed record ResolutionPass(IReadOnlyDictionary<string, string> Folded, bool Judged) {
        public Dictionary<EntityKey, ResolvedEntity> Resolutions { get; } = [];
        public Dictionary<EntityKey, string> Pending { get; } = [];
        public Dictionary<EntityKey, List<EntityCandidate>> Candidates { get; } = [];

        /// <summary>Pending names no tier has claimed yet, in batch order.</summary>
        public List<KeyValuePair<EntityKey, string>> Unresolved =>
            [.. Pending.Where(entry => !Resolutions.ContainsKey(entry.Key))];

        public void Claim(EntityKey key, ResolvedEntity resolution) => Resolutions[key] = resolution;
    }
}
