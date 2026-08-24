// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using DuckDB.NET.Data;
using Kurrent.Kontext.Contracts.V3.Entities;
using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Entities;

public sealed partial class KontextEntityResolver {
    /// <summary>Spelling similarity above which it counts toward the semantic score.</summary>
    const double FuzzyCorroborationFloor = 0.85;

    const int MaxCandidates = 8;

    /// <summary>The merge bar when the spelling agrees with the semantic match.</summary>
    const double CorroboratedMergeThreshold = 0.90;

    /// <summary>
    /// Semantic tier: the nearest same-type entity by embedding merges when close enough, with a
    /// matching spelling lowering the bar. Near misses stay candidates for the disambiguation tier.
    /// </summary>
    async ValueTask ClaimSemanticAsync(ResolutionPass pass, CancellationToken ct) {
        var misses = pass.Undecided;

        if (misses.Count == 0)
            return;

        var embedded = await embeddings
            .GenerateAsync([.. misses.Select(miss => miss.Value.Text)], cancellationToken: ct)
            .ConfigureAwait(false);

        var neighbors = await ResolveSemanticAsync(
            [.. misses.Select((miss, index) => new SemanticQuery(miss.Key, embedded[index].Vector.ToArray()))], ct
        ).ConfigureAwait(false);

        foreach (var (key, _) in misses) {
            var nearest = neighbors.GetValueOrDefault(key);

            var threshold = nearest is { Corroborated: true } && _options.CorroboratedMerging
                ? CorroboratedMergeThreshold
                : _options.SemanticMergeThreshold;

            if (nearest is not null && nearest.Confidence >= threshold) {
                pass.Claim(key, new ResolvedEntity(nearest.EntityId, nearest.Confidence, ResolutionMethod.Semantic));
                continue;
            }

            foreach (var candidate in nearest?.Candidates ?? [])
                pass.AddCandidate(key, candidate);
        }
    }

    /// <summary>Nearest catalog entity per name by embedding, with scores returned raw.</summary>
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

                    // The FLOAT[N] dimension is a type, so it cannot bind. 1 - d/2 converts distance to similarity.
                    command.CommandText =
                        $"""
                         SELECT entity_id,
                                alias,
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

                    string? bestId       = null;
                    var     bestScore    = 0.0;
                    var     corroborated = false;
                    var     neighbours   = new List<EntityCandidate>();

                    using var reader = command.ExecuteReader();
                    while (reader.Read()) {
                        var entityId = reader.GetString(0);
                        var alias    = reader.GetString(1);
                        var semantic = Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture);
                        var fuzzy    = Convert.ToDouble(reader.GetValue(3), CultureInfo.InvariantCulture);
                        var score    = fuzzy >= FuzzyCorroborationFloor ? (semantic + fuzzy) / 2 : semantic;

                        neighbours.Add(new EntityCandidate(entityId, alias, CandidateSource.Semantic));

                        if (score <= bestScore)
                            continue;

                        bestScore    = score;
                        bestId       = entityId;
                        corroborated = fuzzy >= FuzzyCorroborationFloor;
                    }

                    if (bestId is not null)
                        resolved[query.Key] = new SemanticMatch(bestId, bestScore, corroborated, neighbours);
                }

                return resolved;
            }, ct
        ).ConfigureAwait(false);
    }
}
