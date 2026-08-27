// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using Kurrent.Kontext.Contracts.Entities;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Entities.Extraction;
using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Entities;

/// <summary>
/// Resolves names in text to catalog entities through a cascade of tiers, cheapest first. Each
/// tier resolves the names it is confident about and passes the rest down; whatever survives every
/// tier becomes a new entity. One tier per partial file. Stateless: repeat names link through
/// the catalog, which ingestion writes before the next batch resolves.
/// </summary>
public sealed partial class KontextEntityResolver(
    KontextDataSource dataSource,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    EntityResolverOptions? options = null,
    IEntityDisambiguator? disambiguator = null
) {
    readonly EntityResolverOptions _options       = options ?? new EntityResolverOptions();
    readonly IEntityDisambiguator  _disambiguator = disambiguator ?? UniquePrefixDisambiguator.Instance;

    /// <summary>
    /// Runs the batch through every tier and returns the entity each distinct name resolved to.
    /// </summary>
    /// <param name="entities">Extracted mentions to resolve; duplicate name and type pairs collapse.</param>
    /// <param name="ct">Token that cancels the cascade.</param>
    /// <returns>One resolution per distinct name and type pair in the batch.</returns>
    public async ValueTask<IReadOnlyDictionary<EntityKey, ResolvedEntity>> ResolveAsync(
        IEnumerable<ExtractedEntity> entities, CancellationToken ct = default
    ) {
        var names = new NameResolutions(entities);

        await ResolveExactAsync(names, ct).ConfigureAwait(false);
        await ResolveLexicalAsync(names, ct).ConfigureAwait(false);
        await ResolveSemanticAsync(names, ct).ConfigureAwait(false);
        await ResolveAmbiguousAsync(names, ct).ConfigureAwait(false);

        CreateNewEntities(names);

        return names.Resolved;
    }

    static void CreateNewEntities(NameResolutions names) {
        foreach (var (key, name) in names.Unresolved)
            names.ResolveTo(
                key, new ResolvedEntity(EntityId.For(key.EntityType, name.Text), 1.0, ResolutionMethod.Created)
            );
    }

    /// <summary>One name in flight: the possible matches gathered for it and the entity it landed on.</summary>
    sealed class Name(string text) {
        public string                Text       { get; } = text;
        public List<EntityCandidate> Candidates { get; } = [];
        public ResolvedEntity?       Resolution { get; set; }
    }

    /// <summary>
    /// One batch of names moving through the cascade. Every name enters unresolved; a tier either
    /// resolves it or surfaces the possible matches it refused to merge, for the ambiguous tier.
    /// </summary>
    sealed class NameResolutions(IEnumerable<ExtractedEntity> batch) {
        readonly Dictionary<EntityKey, Name> _names = batch
            .Select(entity => (Key: EntityKey.For(entity.EntityType, entity.Text), entity.Text))
            .DistinctBy(entry => entry.Key)
            .ToDictionary(entry => entry.Key, entry => new Name(entry.Text));

        /// <summary>Names no tier has resolved yet, in batch order.</summary>
        public IReadOnlyList<KeyValuePair<EntityKey, Name>> Unresolved =>
            [.. _names.Where(entry => entry.Value.Resolution is null)];

        /// <summary>Every name in the batch with the entity it resolved to.</summary>
        public IReadOnlyDictionary<EntityKey, ResolvedEntity> Resolved =>
            _names.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Resolution
                      ?? throw new InvalidOperationException($"'{entry.Value.Text}' left the cascade unresolved.")
            );

        public void ResolveTo(EntityKey key, ResolvedEntity resolution) {
            var name = _names[key];

            if (name.Resolution is not null)
                throw new InvalidOperationException($"'{name.Text}' was already resolved by an earlier tier.");

            name.Resolution = resolution;
        }

        public void AddPossibleMatch(EntityKey key, EntityCandidate candidate) {
            var candidates = _names[key].Candidates;

            if (candidates.All(known => known.EntityId != candidate.EntityId))
                candidates.Add(candidate);
        }
    }
}
