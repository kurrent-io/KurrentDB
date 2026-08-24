// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using DuckDB.NET.Data;
using Kurrent.Kontext.Contracts.V3.Entities;
using Kurrent.Kontext.Entities.Extraction;

namespace Kurrent.Kontext.Entities;

public sealed partial class KontextEntityResolver {
    /// <summary>Spelling similarity at which two names alone merge.</summary>
    const double LexicalMergeThreshold = 0.94;

    const double StemMatchConfidence = 0.97;

    const double NicknameMatchConfidence = 0.90;

    /// <summary>Names the lexical tier claims outright, plus prefix candidates for the rest.</summary>
    sealed record LexicalResolution(
        Dictionary<EntityKey, ResolvedEntity> Matches,
        Dictionary<EntityKey, List<EntityCandidate>> Prefixes
    );

    /// <summary>
    /// Lexical tier: stem-identical and near-identical spellings merge outright. A unique person
    /// prefix ("Mel" is "Melanie") merges only when no judge exists; otherwise it stays a candidate
    /// for the disambiguation tier.
    /// </summary>
    async ValueTask ClaimLexicalAsync(ResolutionPass pass, CancellationToken ct) {
        if (!_options.LexicalTier)
            return;

        var lexical = await ResolveLexicalAsync([.. pass.Unresolved.Select(entry => entry.Key)], ct).ConfigureAwait(false);

        foreach (var (key, match) in lexical.Matches)
            pass.Claim(key, match);

        // Without a judge, a unique prefix merges on its own.
        if (!pass.Judged)
            foreach (var (key, only) in lexical.Prefixes.Where(entry => entry.Value.Count == 1))
                pass.Claim(key, new ResolvedEntity(only[0].EntityId, NicknameMatchConfidence, ResolutionMethod.FullText));

        foreach (var (key, prefixes) in lexical.Prefixes)
            pass.Candidates[key] = prefixes;
    }

    /// <summary>Spelling matches in three strengths: stem, near-identical, unique prefix.</summary>
    async ValueTask<LexicalResolution> ResolveLexicalAsync(
        IReadOnlyCollection<EntityKey> keys, CancellationToken ct
    ) {
        if (keys.Count == 0)
            return new LexicalResolution([], []);

        return await dts.ExecuteAsync(
            connection => {
                var resolved = new Dictionary<EntityKey, ResolvedEntity>();
                var prefixed = new Dictionary<EntityKey, List<EntityCandidate>>();

                foreach (var group in keys.GroupBy(key => key.EntityType)) {
                    var norms     = new List<string>();
                    var nickables = new List<bool>();
                    var byNorm    = new Dictionary<string, EntityKey>();

                    // Prefix matching applies to person names only.
                    var properNames = group.Key is EntityTypes.Person;

                    foreach (var key in group) {
                        norms.Add(key.NormalizedText);
                        nickables.Add(properNames && key.NormalizedText.Length >= 3 && !key.NormalizedText.Contains(' '));
                        byNorm.TryAdd(key.NormalizedText, key);
                    }

                    using var command = connection.CreateCommand();

                    // Fuzzy matching compares single words only, and prefix matching runs both ways.
                    command.CommandText =
                        """
                        WITH aliases AS (
                            SELECT entity_id, alias, lower(alias) AS alias_lower, fold(alias) AS alias_norm
                            FROM ldb.main.entities
                            WHERE entity_type = $entity_type
                        ),
                        claims AS (
                            SELECT norm_text, nickable, fold(norm_text) AS folded
                            FROM (SELECT unnest(CAST($norms AS VARCHAR[])) AS norm_text,
                                         unnest(CAST($nickables AS BOOLEAN[])) AS nickable)
                        ),
                        scored AS (
                            SELECT c.norm_text,
                                   a.entity_id,
                                   a.alias,
                                   a.alias_norm = c.folded AND c.folded <> '' AS stem_hit,
                                   CASE WHEN NOT contains(c.norm_text, ' ') AND NOT contains(a.alias_lower, ' ')
                                        THEN jaro_winkler_similarity(a.alias_lower, c.norm_text)
                                        ELSE 0
                                   END AS jw,
                                   c.nickable AND (
                                       (starts_with(a.alias_lower, c.norm_text)
                                        AND length(a.alias) > length(c.norm_text))
                                    OR (starts_with(c.norm_text, a.alias_lower)
                                        AND length(c.norm_text) > length(a.alias)
                                        AND length(a.alias) >= 3
                                        AND NOT contains(a.alias_lower, ' '))
                                   ) AS prefix_hit
                            FROM aliases a
                            CROSS JOIN claims c
                        )
                        SELECT norm_text, entity_id, alias, stem_hit, jw, prefix_hit
                        FROM scored
                        WHERE stem_hit OR prefix_hit OR jw >= $jw_floor
                        """;

                    command.Parameters.Add(new DuckDBParameter("norms", norms));
                    command.Parameters.Add(new DuckDBParameter("nickables", nickables));
                    command.Parameters.Add(new DuckDBParameter("entity_type", group.Key));
                    command.Parameters.Add(new DuckDBParameter("jw_floor", LexicalMergeThreshold));

                    var stems    = new Dictionary<EntityKey, string>();
                    var fuzzy    = new Dictionary<EntityKey, (string EntityId, double Score)>();
                    var prefixes = new Dictionary<EntityKey, List<EntityCandidate>>();

                    using (var reader = command.ExecuteReader())
                        while (reader.Read()) {
                            var key      = byNorm[reader.GetString(0)];
                            var entityId = reader.GetString(1);
                            var alias    = reader.GetString(2);

                            if (reader.GetBoolean(3))
                                stems.TryAdd(key, entityId);

                            var jw = Convert.ToDouble(reader.GetValue(4), CultureInfo.InvariantCulture);

                            if (jw >= LexicalMergeThreshold && (!fuzzy.TryGetValue(key, out var best) || jw > best.Score))
                                fuzzy[key] = (entityId, jw);

                            if (reader.GetBoolean(5))
                                Remember(prefixes, key, new EntityCandidate(entityId, alias, jw));
                        }

                    // Stem and near-identical merge outright while a prefix stays a candidate.
                    foreach (var key in group) {
                        if (stems.TryGetValue(key, out var stemId))
                            resolved[key] = new ResolvedEntity(stemId, StemMatchConfidence, ResolutionMethod.FullText);
                        else if (fuzzy.TryGetValue(key, out var near))
                            resolved[key] = new ResolvedEntity(near.EntityId, near.Score, ResolutionMethod.FullText);
                        else if (prefixes.TryGetValue(key, out var candidates))
                            prefixed[key] = candidates;
                    }
                }

                return new LexicalResolution(resolved, prefixed);
            }, ct
        ).ConfigureAwait(false);
    }

    /// <summary>The folded shape of each text, one call for the batch.</summary>
    async ValueTask<IReadOnlyDictionary<string, string>> FoldAsync(List<string> texts, CancellationToken ct) {
        if (texts.Count == 0)
            return new Dictionary<string, string>();

        return await dts.ExecuteAsync<IReadOnlyDictionary<string, string>>(
            connection => {
                using var command = connection.CreateCommand();

                command.CommandText =
                    """
                    SELECT text, fold(text)
                    FROM (SELECT DISTINCT unnest(CAST($texts AS VARCHAR[])) AS text)
                    """;

                command.Parameters.Add(new DuckDBParameter("texts", texts));

                var folded = new Dictionary<string, string>();

                using var reader = command.ExecuteReader();
                while (reader.Read())
                    folded[reader.GetString(0)] = reader.GetString(1);

                return folded;
            }, ct
        ).ConfigureAwait(false);
    }
}
