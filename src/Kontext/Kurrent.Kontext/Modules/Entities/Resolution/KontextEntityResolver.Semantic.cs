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

    /// <summary>A catalog neighbour of one name: agreeing spelling averages into the semantic score.</summary>
    sealed record SemanticNeighbour(string EntityId, string Alias, double Semantic, double Fuzzy) {
        public bool Corroborated => Fuzzy >= FuzzyCorroborationFloor;

        public double Score => Corroborated ? (Semantic + Fuzzy) / 2 : Semantic;

        public EntityCandidate AsCandidate => new(EntityId, Alias, CandidateSource.Semantic);
    }

    /// <summary>
    /// Semantic tier: the nearest same-type entity by embedding merges when close enough, with a
    /// matching spelling lowering the bar. Near misses stay candidates for the disambiguation tier.
    /// </summary>
    async ValueTask ResolveSemanticAsync(NameResolutions names, CancellationToken ct) {
        var misses = names.Unresolved;

        if (misses.Count == 0)
            return;

        var embedded = await embeddings
            .GenerateAsync([.. misses.Select(miss => miss.Value.Text)], cancellationToken: ct)
            .ConfigureAwait(false);

        var neighbors = await LookupSemanticAsync(
            [.. misses.Zip(embedded, (miss, embedding) => new SemanticQuery(miss.Key, embedding.Vector.ToArray()))], ct
        ).ConfigureAwait(false);

        foreach (var (key, _) in misses) {
            var nearest = neighbors.GetValueOrDefault(key);

            var threshold = nearest is { Corroborated: true } && _options.CorroboratedMerging
                ? CorroboratedMergeThreshold
                : _options.SemanticMergeThreshold;

            if (nearest is not null && nearest.Confidence >= threshold) {
                names.ResolveTo(key, new ResolvedEntity(nearest.EntityId, nearest.Confidence, ResolutionMethod.Semantic));
                continue;
            }

            foreach (var candidate in nearest?.Candidates ?? [])
                names.AddPossibleMatch(key, candidate);
        }
    }

    /// <summary>Nearest catalog entity per name by embedding, with scores returned raw.</summary>
    internal async ValueTask<IReadOnlyDictionary<EntityKey, SemanticMatch>> LookupSemanticAsync(
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

                    var neighbours = new List<SemanticNeighbour>();

                    using var reader = command.ExecuteReader();
                    while (reader.Read()) {
                        var semantic = Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture);
                        var fuzzy    = Convert.ToDouble(reader.GetValue(3), CultureInfo.InvariantCulture);

                        neighbours.Add(new SemanticNeighbour(reader.GetString(0), reader.GetString(1), semantic, fuzzy));
                    }

                    // A nonpositive score is no match at all, not a weak one.
                    if (neighbours.Where(neighbour => neighbour.Score > 0).MaxBy(neighbour => neighbour.Score) is { } nearest)
                        resolved[query.Key] = new SemanticMatch(
                            nearest.EntityId, nearest.Score, nearest.Corroborated,
                            [.. neighbours.Select(neighbour => neighbour.AsCandidate)]
                        );
                }

                return resolved;
            }, ct
        ).ConfigureAwait(false);
    }
}
