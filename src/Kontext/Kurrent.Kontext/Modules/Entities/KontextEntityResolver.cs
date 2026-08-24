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

/// <summary>
/// Best semantic candidate for a surface form. Confidence is the combined score, thresholding is
/// the caller's call; <see cref="Corroborated"/> says the spelling agreed with the vector.
/// <see cref="Candidates"/> is the whole same-type neighbourhood the search returned, near misses
/// included, so a later tier can reconsider what this one would not merge.
/// </summary>
public sealed record SemanticMatch(
    string EntityId,
    double Confidence,
    bool Corroborated = false,
    IReadOnlyList<EntityCandidate>? Candidates = null
);

/// <summary>The resolver's opt-in behaviour. The tier thresholds are constants, not knobs — they are measured.</summary>
public sealed class EntityResolverOptions {
    /// <summary>
    /// Whether the lexical tier runs between exact and semantic matching: stem-identical forms,
    /// near-identical single words, and unique-prefix person nicknames.
    /// </summary>
    public bool LexicalTier { get; set; } = true;

    /// <summary>
    /// Whether a spelling that corroborates the vector lowers the merge bar. Off, every semantic
    /// merge faces the uncorroborated threshold alone.
    /// </summary>
    public bool CorroboratedMerging { get; set; } = true;

    /// <summary>
    /// Whether a model gets the last word on names no deterministic tier would merge. Needs a
    /// disambiguator; without one the tier is skipped and a unique prefix merges on its own, the
    /// way it did before the tier existed.
    /// </summary>
    public bool LlmTier { get; set; } = true;

    /// <summary>
    /// Vector similarity above which an uncorroborated match merges without review. The default is
    /// measured, not chosen: an embedding puts related concepts close ("paintings", "art"), and a
    /// wrong merge corrupts the catalog permanently while a missed one costs only an alias.
    /// </summary>
    public double SemanticMergeThreshold { get; set; } = 0.97;

    /// <summary>
    /// The cascade as it shipped before the 2026-08-21 resolution work: an exact alias hit, then
    /// vector similarity, with nothing lexical in between. Kept measurable on purpose — the same
    /// reason the retrieval chain keeps its Legacy composition — so the benchmark can price what
    /// the lexical tier is worth instead of assuming it.
    /// </summary>
    public static EntityResolverOptions Legacy =>
        new() { LexicalTier = false, CorroboratedMerging = false, SemanticMergeThreshold = 0.95 };
}

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
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    EntityResolverOptions? options = null,
    IEntityDisambiguator? disambiguator = null
) {
    readonly EntityResolverOptions _options = options ?? new EntityResolverOptions();

    /// <summary>Best lexical candidate for a surface form: a stem-identical, near-identical, or unique-prefix alias.</summary>
    sealed record LexicalMatch(string EntityId, double Confidence);

    /// <summary>
    /// What the lexical tier concluded: the names it claims outright, and the prefix candidates it
    /// found for the rest. A prefix is the weakest evidence this resolver has, so it is a claim
    /// only when no model is available to judge it.
    /// </summary>
    sealed record LexicalResolution(
        Dictionary<EntityKey, LexicalMatch> Matches,
        Dictionary<EntityKey, List<EntityCandidate>> Prefixes
    );

    /// <summary>
    /// How similar the spelling must be before it counts toward the score. At or above this, the
    /// spelling and vector scores average. Below it, the vector score stands alone.
    /// </summary>
    const double FuzzyCorroborationFloor = 0.85;

    /// <summary>How many nearest entities the vector search returns per name before the best one is picked.</summary>
    const int MaxCandidates = 8;

    /// <summary>
    /// The merge bar when the spelling corroborates the vector: two independent signals agreeing
    /// buy a lower combined threshold than the vector alone.
    /// </summary>
    const double CorroboratedMergeThreshold = 0.90;

    /// <summary>Jaro-Winkler similarity at which two same-type spellings alone name the same entity.</summary>
    const double LexicalMergeThreshold = 0.94;

    /// <summary>A stem-level hit is deterministic but not literal — confidently below exact, above any guess.</summary>
    const double StemMatchConfidence = 0.97;

    /// <summary>A unique-prefix nickname hit rests on one corroborating signal, the weakest merge this resolver makes on its own.</summary>
    const double NicknameMatchConfidence = 0.90;

    /// <summary>
    /// A model read both names and said they are the same thing. Above a nickname guess, which
    /// rests on spelling alone, and below a stem hit, which is deterministic.
    /// </summary>
    const double LlmMatchConfidence = 0.95;

    /// <summary>Names created here, so later mentions link instead of re-creating while the projector catches up.</summary>
    readonly Dictionary<EntityKey, string> _created = [];

    /// <summary>The created names by folded shape, so "pottery classes" links to the "pottery class" created one batch earlier.</summary>
    readonly Dictionary<EntityKey, string> _createdFolded = [];

    public async ValueTask<IReadOnlyDictionary<EntityKey, ResolvedEntity>> ResolveAsync(
        IEnumerable<ExtractedEntity> entities, CancellationToken ct = default
    ) {
        var resolutions = new Dictionary<EntityKey, ResolvedEntity>();
        var pending     = new Dictionary<EntityKey, string>();
        var batch       = entities as IReadOnlyCollection<ExtractedEntity> ?? [.. entities];

        // The fold is a SQL expression, so the whole batch folds in one call rather than one per
        // span. Only the lexical tier reads the folded shapes, so nothing else pays for them.
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

        // With no judge, a UNIQUE prefix merges on its own and stops here — the behaviour that
        // shipped before this tier, reported as the spelling guess it is. Turning the model off must
        // cost recall on the names only a model can settle, never the nickname that never needed one,
        // and must not make the name pay for an embedding nothing will read.
        var judged = disambiguator is not null && _options.LlmTier;

        if (!judged)
            foreach (var (key, only) in lexical.Prefixes.Where(entry => entry.Value.Count == 1))
                resolutions[key] = new ResolvedEntity(only[0].EntityId, NicknameMatchConfidence, ResolutionMethod.FullText);

        // Every entity a cheaper tier surfaced without merging, per name: the prefix hits, plus the
        // vector neighbourhood added below.
        var candidates = lexical.Prefixes.ToDictionary(
            entry => entry.Key, entry => new List<EntityCandidate>(entry.Value));

        var misses = afterExact.Where(entry => !resolutions.ContainsKey(entry.Key)).ToList();

        if (misses.Count == 0)
            return resolutions;

        // The semantic pass embeds the spans as written, the same form the catalog embedded.
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

    /// <summary>
    /// Hands the names no deterministic tier would merge to the model, each with the entities those
    /// tiers surfaced and refused. A name no tier surfaced anything for is not asked about: there is
    /// nothing to choose between, and it becomes a new entity.
    /// </summary>
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

    /// <summary>
    /// The folded shape of each text, from the engine that stored the catalog's own. One call for
    /// the batch: a fold reads no rows, so the cost is the round trip, not the row count.
    /// </summary>
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
    /// Finds the catalog entity whose spelling alone claims each name, in three strengths. Only
    /// aliases of the same type compete; names with no claim are absent from the result.
    /// <list type="number">
    ///   <item><description>Stem-identical: the folded shapes match ("pottery classes" is "pottery class").</description></item>
    ///   <item><description>Near-identical: Jaro-Winkler at or above <see cref="LexicalMergeThreshold"/> ("Mell" is "Mel").</description></item>
    ///   <item><description>Unique prefix: a single short name extends to exactly ONE entity ("Mel" is "Melanie") —
    ///   two candidates mean ambiguity, and ambiguity never merges.</description></item>
    /// </list>
    /// </summary>
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

                    // Prefix claims are a PERSON-name phenomenon: "Mel" extends to "Melanie".
                    // Organizations were included until the resolution benchmark caught the cost —
                    // a bare label prefixes every org named after it ("Riverside" claiming
                    // "Riverside Library", "Riverside Clinic", "Riverside Cafe"), and each merge
                    // grows the blob the next one joins. Common nouns share letters, not identity, so every other
                    // type gets stem and near-identity matching only.
                    var properNames = group.Key is EntityTypes.Person;

                    foreach (var key in group) {
                        norms.Add(key.NormalizedText);
                        nickables.Add(properNames && key.NormalizedText.Length >= 3 && !key.NormalizedText.Contains(' '));
                        byNorm.TryAdd(key.NormalizedText, key);
                    }

                    using var command = connection.CreateCommand();

                    // The near-identity (Jaro-Winkler) tier compares single words only: JW's
                    // prefix bonus makes "adoption interview" and "adoption meeting" near-twins,
                    // but shared phrase heads are shared context, not shared identity — multiword
                    // forms must earn their merge through the stem or semantic tiers instead.
                    // prefix_hit runs both ways: a short name claims the longer alias that extends
                    // it ("Mel" is "Melanie"), and a long name claims the shorter single-word
                    // alias it extends ("Melanie" is "Mel") — whichever form the catalog met first.
                    // Each tier is scored once and filtered on, so the predicate that admits a row
                    // is the same expression the reader grades it by.
                    command.CommandText =
                        """
                        SELECT norm_text, entity_id, alias, stem_hit, jw, prefix_hit
                        FROM (SELECT k.norm_text,
                                     e.entity_id,
                                     e.alias,
                                     e.alias_norm = k.folded AND k.folded <> '' AS stem_hit,
                                     CASE WHEN NOT contains(k.norm_text, ' ') AND NOT contains(lower(e.alias), ' ')
                                          THEN jaro_winkler_similarity(lower(e.alias), k.norm_text)
                                          ELSE 0 END AS jw,
                                     k.nickable AND (
                                          (starts_with(lower(e.alias), k.norm_text)
                                           AND length(e.alias) > length(k.norm_text))
                                       OR (starts_with(k.norm_text, lower(e.alias))
                                           AND length(k.norm_text) > length(e.alias)
                                           AND length(e.alias) >= 3
                                           AND NOT contains(lower(e.alias), ' '))) AS prefix_hit
                              -- Both sides fold in their own subquery, once per row rather than
                              -- once per candidate pair: the join below is a cross product, and
                              -- the fold is the expensive half of it.
                              FROM (SELECT entity_id, alias, fold(alias) AS alias_norm
                                    FROM ldb.main.entities
                                    WHERE entity_type = $entity_type) e,
                                   (SELECT norm_text, nickable, fold(norm_text) AS folded
                                    FROM (SELECT unnest(CAST($norms AS VARCHAR[])) AS norm_text,
                                                 unnest(CAST($nickables AS BOOLEAN[])) AS nickable)) k) claims
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

                    // A stem or a near-identical spelling is evidence enough to merge on. A prefix
                    // is not: it is reported as a candidate, and only the last tier decides whether
                    // "Will" is this Will. Ambiguity is reported too — several entities claiming one
                    // name is exactly what a judge needs to see.
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
