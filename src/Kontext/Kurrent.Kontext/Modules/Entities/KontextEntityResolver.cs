// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Globalization;
using DuckDB.NET.Data;
using Kurrent.Kontext.Contracts.V3.Entities;
using Kurrent.Kontext.Data;
using Kurrent.Kontext.Infrastructure.Data;
using Kurrent.Kontext.Entities.Extraction;
using Microsoft.Extensions.AI;

namespace Kurrent.Kontext.Entities;

/// <summary>A name to resolve semantically, with its embedding.</summary>
public sealed record SemanticQuery(EntityKey Key, float[] Embedding);

/// <summary>Best semantic match for a name, with the runners-up in <see cref="Candidates"/> for later tiers.</summary>
public sealed record SemanticMatch(
    string EntityId,
    double Confidence,
    bool Corroborated = false,
    IReadOnlyList<EntityCandidate>? Candidates = null
);

public sealed class EntityResolverOptions {
    /// <summary>Runs the lexical tier between exact and semantic matching.</summary>
    public bool LexicalTier { get; set; } = true;

    /// <summary>Lets a matching spelling lower the semantic merge bar.</summary>
    public bool CorroboratedMerging { get; set; } = true;

    /// <summary>Lets a model decide names no other tier would merge, skipped when no disambiguator is given.</summary>
    public bool LlmTier { get; set; } = true;

    /// <summary>Similarity above which a semantic match merges.</summary>
    public double SemanticMergeThreshold { get; set; } = 0.97;

    /// <summary>The old cascade, kept for benchmarking.</summary>
    public static EntityResolverOptions Legacy =>
        new() { LexicalTier = false, CorroboratedMerging = false, SemanticMergeThreshold = 0.95 };
}

public sealed record ResolvedEntity(string EntityId, double Confidence, ResolutionMethod Method);

/// <summary>Resolves names in text to catalog entities: exact, lexical, semantic, else creates one.</summary>
public sealed class KontextEntityResolver(
    KontextDataSource dts,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    EntityResolverOptions? options = null,
    IEntityDisambiguator? disambiguator = null
) {
    readonly EntityResolverOptions _options = options ?? new EntityResolverOptions();

    sealed record LexicalMatch(string EntityId, double Confidence);

    /// <summary>Names the lexical tier claims outright, plus prefix candidates for the rest.</summary>
    sealed record LexicalResolution(
        Dictionary<EntityKey, LexicalMatch> Matches,
        Dictionary<EntityKey, List<EntityCandidate>> Prefixes
    );

    /// <summary>Spelling similarity above which it counts toward the semantic score.</summary>
    const double FuzzyCorroborationFloor = 0.85;

    const int MaxCandidates = 8;

    /// <summary>The merge bar when the spelling agrees with the semantic match.</summary>
    const double CorroboratedMergeThreshold = 0.90;

    /// <summary>Spelling similarity at which two names alone merge.</summary>
    const double LexicalMergeThreshold = 0.94;

    const double StemMatchConfidence = 0.97;

    const double NicknameMatchConfidence = 0.90;

    const double LlmMatchConfidence = 0.95;

    /// <summary>Names created here, so later mentions link instead of re-creating.</summary>
    readonly Dictionary<EntityKey, string> _created = [];

    /// <summary>Created names by folded shape, so "pottery classes" links to "pottery class".</summary>
    readonly Dictionary<EntityKey, string> _createdFolded = [];

    public async ValueTask<IReadOnlyDictionary<EntityKey, ResolvedEntity>> ResolveAsync(
        IEnumerable<ExtractedEntity> entities, CancellationToken ct = default
    ) {
        var resolutions = new Dictionary<EntityKey, ResolvedEntity>();
        var pending     = new Dictionary<EntityKey, string>();
        var batch       = entities as IReadOnlyCollection<ExtractedEntity> ?? [.. entities];

        var folded = _options.LexicalTier
            ? await FoldAsync([.. batch.Select(entity => entity.Text)], ct).ConfigureAwait(false)
            : new Dictionary<string, string>();

        foreach (var entity in batch) {
            var key = EntityKey.For(entity.EntityType, entity.Text);

            if (_created.TryGetValue(key, out var createdId))
                resolutions.TryAdd(key, new ResolvedEntity(createdId, 1.0, ResolutionMethod.Exact));
            else if (_options.LexicalTier && _createdFolded.TryGetValue(FoldedKey(key, folded[entity.Text]), out var foldedId))
                resolutions.TryAdd(key, new ResolvedEntity(foldedId, StemMatchConfidence, ResolutionMethod.FullText));
            else
                pending.TryAdd(key, entity.Text);
        }

        var aliases = await ResolveExactAsync(pending.Keys, ct).ConfigureAwait(false);

        foreach (var (key, entityId) in aliases)
            resolutions[key] = new ResolvedEntity(entityId, 1.0, ResolutionMethod.Exact);

        var afterExact = pending.Where(entry => !aliases.ContainsKey(entry.Key)).ToList();

        if (afterExact.Count == 0)
            return resolutions;

        var lexical = _options.LexicalTier
            ? await ResolveLexicalAsync(afterExact.Select(entry => entry.Key).ToList(), ct).ConfigureAwait(false)
            : new LexicalResolution([], []);

        foreach (var (key, match) in lexical.Matches)
            resolutions[key] = new ResolvedEntity(match.EntityId, match.Confidence, ResolutionMethod.FullText);

        // Without a judge, a unique prefix merges on its own.
        var judged = disambiguator is not null && _options.LlmTier;

        if (!judged)
            foreach (var (key, only) in lexical.Prefixes.Where(entry => entry.Value.Count == 1))
                resolutions[key] = new ResolvedEntity(only[0].EntityId, NicknameMatchConfidence, ResolutionMethod.FullText);

        var candidates = lexical.Prefixes.ToDictionary(
            entry => entry.Key, entry => new List<EntityCandidate>(entry.Value));

        var misses = afterExact.Where(entry => !resolutions.ContainsKey(entry.Key)).ToList();

        if (misses.Count == 0)
            return resolutions;

        var embedded = await embeddings
            .GenerateAsync(misses.Select(miss => miss.Value).ToList(), cancellationToken: ct)
            .ConfigureAwait(false);

        var neighbors = await ResolveSemanticAsync(
            misses.Select((miss, index) => new SemanticQuery(miss.Key, embedded[index].Vector.ToArray())).ToList(), ct
        ).ConfigureAwait(false);

        var undecided = new List<KeyValuePair<EntityKey, string>>();

        foreach (var (key, text) in misses) {
            var nearest = neighbors.GetValueOrDefault(key);

            var threshold = nearest is { Corroborated: true } && _options.CorroboratedMerging
                ? CorroboratedMergeThreshold
                : _options.SemanticMergeThreshold;

            if (nearest is not null && nearest.Confidence >= threshold) {
                resolutions[key] = new ResolvedEntity(nearest.EntityId, nearest.Confidence, ResolutionMethod.Semantic);
                continue;
            }

            foreach (var candidate in nearest?.Candidates ?? [])
                Remember(candidates, key, candidate);

            undecided.Add(new(key, text));
        }

        var chosen = judged
            ? await DisambiguateAsync(undecided, candidates, ct).ConfigureAwait(false)
            : new Dictionary<EntityKey, string>();

        foreach (var (key, text) in undecided) {
            if (chosen.TryGetValue(key, out var entityId)) {
                resolutions[key] = new ResolvedEntity(entityId, LlmMatchConfidence, ResolutionMethod.Llm);
                continue;
            }

            var created = EntityId.For(key.EntityType, text);

            _created[key] = created;

            if (_options.LexicalTier)
                _createdFolded.TryAdd(FoldedKey(key, folded[text]), created);

            resolutions[key] = new ResolvedEntity(created, 1.0, ResolutionMethod.Created);
        }

        return resolutions;
    }

    static void Remember(
        Dictionary<EntityKey, List<EntityCandidate>> candidates, EntityKey key, EntityCandidate candidate
    ) {
        var forKey = candidates.TryGetValue(key, out var existing) ? existing : candidates[key] = [];

        if (forKey.All(known => known.EntityId != candidate.EntityId))
            forKey.Add(candidate);
    }

    /// <summary>Hands undecided names with candidates to the model. A name with none becomes a new entity.</summary>
    async ValueTask<IReadOnlyDictionary<EntityKey, string>> DisambiguateAsync(
        List<KeyValuePair<EntityKey, string>> undecided,
        Dictionary<EntityKey, List<EntityCandidate>> candidates,
        CancellationToken ct
    ) {
        var pending = undecided
            .Where(entry => candidates.ContainsKey(entry.Key))
            .Select(entry => new Disambiguation(entry.Key, entry.Value, candidates[entry.Key]))
            .ToList();

        return pending.Count == 0
            ? new Dictionary<EntityKey, string>()
            : await disambiguator!.ResolveAsync(pending, ct).ConfigureAwait(false);
    }

    static EntityKey FoldedKey(EntityKey key, string foldedText) => new(key.EntityType, foldedText);

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

    /// <summary>Exact alias lookup per entity type. Unmatched names are absent.</summary>
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

    /// <summary>Spelling matches in three strengths: stem, near-identical, unique prefix.</summary>
    async ValueTask<LexicalResolution> ResolveLexicalAsync(
        IReadOnlyCollection<EntityKey> keys, CancellationToken ct
    ) {
        if (keys.Count == 0)
            return new LexicalResolution([], []);

        return await dts.ExecuteAsync(
            connection => {
                var resolved = new Dictionary<EntityKey, LexicalMatch>();
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
                            resolved[key] = new LexicalMatch(stemId, StemMatchConfidence);
                        else if (fuzzy.TryGetValue(key, out var near))
                            resolved[key] = new LexicalMatch(near.EntityId, near.Score);
                        else if (prefixes.TryGetValue(key, out var candidates))
                            prefixed[key] = candidates;
                    }
                }

                return new LexicalResolution(resolved, prefixed);
            }, ct
        ).ConfigureAwait(false);
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

                        neighbours.Add(new EntityCandidate(entityId, alias, score));

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
