// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using DuckDB.NET.Data;
using Kurrent.Kontext.Contracts.V3.Entities;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Modules.Entities.Extraction;
using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Modules.Entities;

/// <summary>Surface form the exact pass missed, plus its embedding for the vector search.</summary>
public sealed record SemanticQuery(EntityKey Key, float[] Embedding);

/// <summary>Best semantic candidate for a surface form. Confidence is the combined score, thresholding is the caller's call.</summary>
public sealed record SemanticMatch(string EntityId, double Confidence);

/// <summary>Entity a name resolved to, with method and confidence.</summary>
public sealed record ResolvedEntity(string EntityId, double Confidence, ResolutionMethod Method);

/// <summary>
/// Resolves names mentioned in text, such as "Tim" or "Acme Corp", to entities in the catalog.
/// Each name takes the first match that applies.
/// <list type="number">
///   <item><description>A name matching a known alias links to its entity.</description></item>
///   <item><description>A close semantic match merges with the entity it resembles.</description></item>
///   <item><description>An unrecognized name becomes a new entity with a deterministic id.</description></item>
/// </list>
/// Created names are remembered, so repeat mentions link instead of duplicating. Every name
/// resolves to an entity id with a confidence and the method that produced it.
/// </summary>
public sealed class KontextEntityResolver(
    KontextDataSource dts,
    IEmbeddingGenerator<string, Embedding<float>> embeddings
) {
    /// <summary>
    /// How similar the spelling must be before it counts toward the score. At or above this, the
    /// spelling and vector scores average. Below it, the vector score stands alone.
    /// </summary>
    const double FuzzyCorroborationFloor = 0.85;

    /// <summary>How many nearest entities the vector search returns per name before the best one is picked.</summary>
    const int MaxCandidates = 8;

    /// <summary>
    /// Score above which a match is trusted enough to merge without review. Below it, the name
    /// creates a new entity instead.
    /// </summary>
    const double AutoMergeThreshold = 0.95;

    /// <summary>Names created here, so later mentions link instead of re-creating while the projector catches up.</summary>
    readonly Dictionary<EntityKey, string> _created = [];

    public async ValueTask<IReadOnlyDictionary<EntityKey, ResolvedEntity>> ResolveAsync(
        IEnumerable<ExtractedEntity> entities, CancellationToken ct = default
    ) {
        var resolutions = new Dictionary<EntityKey, ResolvedEntity>();
        var pending     = new Dictionary<EntityKey, string>();

        foreach (var entity in entities) {
            var key = EntityKey.For(entity.EntityType, entity.Text);

            if (_created.TryGetValue(key, out var createdId))
                resolutions.TryAdd(key, new ResolvedEntity(createdId, 1.0, ResolutionMethod.Exact));
            else
                pending.TryAdd(key, entity.Text);
        }

        var aliases = await ResolveExactAsync(pending.Keys, ct).ConfigureAwait(false);

        foreach (var (key, entityId) in aliases)
            resolutions[key] = new ResolvedEntity(entityId, 1.0, ResolutionMethod.Exact);

        var misses = pending.Where(entry => !aliases.ContainsKey(entry.Key)).ToList();

        if (misses.Count == 0)
            return resolutions;

        // The semantic pass embeds the spans as written, the same form the catalog embedded.
        var embedded = await embeddings
            .GenerateAsync(misses.Select(miss => miss.Value).ToList(), cancellationToken: ct)
            .ConfigureAwait(false);

        var neighbors = await ResolveSemanticAsync(
            misses.Select((miss, index) => new SemanticQuery(miss.Key, embedded[index].Vector.ToArray())).ToList(), ct
        ).ConfigureAwait(false);

        foreach (var (key, text) in misses) {
            var nearest = neighbors.GetValueOrDefault(key);

            resolutions[key] = nearest is not null && nearest.Confidence >= AutoMergeThreshold
                ? new ResolvedEntity(nearest.EntityId, nearest.Confidence, ResolutionMethod.Semantic)
                : new ResolvedEntity(_created[key] = EntityId.For(key.EntityType, text), 1.0, ResolutionMethod.Created);
        }

        return resolutions;
    }

    /// <summary>
    /// Looks up each name in the catalog and returns the entity id whose alias matches it exactly,
    /// ignoring case and spacing. Only aliases of the same entity type count, so "apple" the
    /// organization never matches "apple" the object. Names with no match are absent from the
    /// result. When several entities share an alias, the first one scanned wins.
    /// </summary>
    public async ValueTask<IReadOnlyDictionary<EntityKey, string>> ResolveExactAsync(
        IReadOnlyCollection<EntityKey> keys, CancellationToken ct = default
    ) {
        if (keys.Count == 0)
            return new Dictionary<EntityKey, string>();

        return await dts.ExecuteAsync<IReadOnlyDictionary<EntityKey, string>>(
            connection => {
                var resolved = new Dictionary<EntityKey, string>();

                foreach (var group in keys.GroupBy(key => key.EntityType)) {
                    using var command = connection.CreateCommand();

                    command.CommandText =
                        """
                        SELECT lower(alias) AS matched, entity_id
                        FROM ldb.main.entities
                        WHERE entity_type = $entity_type
                          AND array_contains(CAST($aliases AS VARCHAR[]), lower(alias))
                        """;

                    command.Parameters.Add(new DuckDBParameter("entity_type", group.Key));
                    command.Parameters.Add(new DuckDBParameter("aliases", group.Select(key => key.NormalizedText).ToList()));

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                        resolved.TryAdd(new EntityKey(group.Key, reader.GetString(0)), reader.GetString(1));
                }

                return resolved;
            }, ct
        ).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the catalog entity closest in meaning to each name and returns it with a similarity
    /// score. Only entities of the same type compete.
    /// <list type="number">
    ///   <item><description>A vector search returns the nearest aliases by embedding.</description></item>
    ///   <item><description>Candidates whose spelling also matches get their score boosted.</description></item>
    ///   <item><description>The best-scoring candidate wins per name.</description></item>
    /// </list>
    /// Names with no same-type candidate are absent from the result. Scores come back raw, the
    /// caller decides what is close enough.
    /// </summary>
    public async ValueTask<IReadOnlyDictionary<EntityKey, SemanticMatch>> ResolveSemanticAsync(
        IReadOnlyCollection<SemanticQuery> queries, CancellationToken ct = default
    ) {
        if (queries.Count == 0)
            return new Dictionary<EntityKey, SemanticMatch>();

        return await dts.ExecuteAsync<IReadOnlyDictionary<EntityKey, SemanticMatch>>(
            connection => {
                var resolved = new Dictionary<EntityKey, SemanticMatch>();

                foreach (var query in queries) {
                    using var command = connection.CreateCommand();

                    // The type equality pushes down as a true prefilter, so only same-type rows
                    // compete for k. The FLOAT[N] dimension is a type, not a value, it cannot bind.
                    // _distance is squared L2 (the metric kontext's vector indexes are built for),
                    // and the embeddings are L2-normalized, so 1 - d/2 is the cosine similarity.
                    command.CommandText =
                        $"""
                         SELECT entity_id,
                                1 - _distance / 2 AS semantic,
                                jaro_winkler_similarity(lower(alias), $text) AS fuzzy
                         FROM lance_vector_search('ldb.main.entities', 'embedding', CAST($embedding AS FLOAT[{query.Embedding.Length}]),
                                                  k := $k, prefilter := true)
                         WHERE entity_type = $entity_type
                         ORDER BY _distance ASC
                         """;

                    command.Parameters.Add(new DuckDBParameter("embedding", query.Embedding));
                    command.Parameters.Add(new DuckDBParameter("k", MaxCandidates));
                    command.Parameters.Add(new DuckDBParameter("text", query.Key.NormalizedText));
                    command.Parameters.Add(new DuckDBParameter("entity_type", query.Key.EntityType));

                    string? bestId    = null;
                    var     bestScore = 0.0;

                    using var reader = command.ExecuteReader();
                    while (reader.Read()) {
                        var semantic = Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture);
                        var fuzzy    = Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture);
                        var score    = fuzzy >= FuzzyCorroborationFloor ? (semantic + fuzzy) / 2 : semantic;

                        if (score <= bestScore)
                            continue;

                        bestScore = score;
                        bestId    = reader.GetString(0);
                    }

                    if (bestId is not null)
                        resolved[query.Key] = new SemanticMatch(bestId, bestScore);
                }

                return resolved;
            }, ct
        ).ConfigureAwait(false);
    }
}
