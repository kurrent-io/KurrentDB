// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Data.Common;
using System.Globalization;
using System.Runtime.CompilerServices;
using DuckDB.NET.Data;
using Google.Protobuf.WellKnownTypes;

namespace Kurrent.Kontext.Data;

/// <summary>
/// Access the memories read model.
///
/// No vector store: queries go straight to DuckDB through the host-owned
/// <see cref="KontextConnectionPool"/>, which owns the engine problems (bootstrap, retries, lifecycle).
///
/// Read-only by design: KurrentDB is the source of truth for memories, and the projector
/// updates this read model without going through the store.
///
/// Operations:
/// - search, three modes picked by what the caller supplies: vector (embedding only),
///   full-text (keywords only), hybrid (both, blended) — each with its own Lance knobs
/// - get by id(s)
/// - lineage: the whole supersession family of a memory, chronological
/// - list by tags and types with limit and sort
///
/// SQL rules in this file:
/// - every statement is a const inside the method that runs it
/// - alias, table, and columns are hardcoded — normal DuckDB naming (underscore_lowercase)
/// - every value travels as a named $parameter, never inlined into the text (validated live:
///   even Lance's named arguments bind); only a clause or the FLOAT[N] dimension is interpolated
/// </summary>
public sealed class KontextDataStoreV2(KontextConnectionPool connections) {
    /// <summary>
    /// Vector search: ranks memories by embedding similarity to the query vector alone.
    ///
    /// Recall's view of the world:
    /// - retracted and superseded memories never surface
    /// - every requested tag must be present
    /// - results arrive nearest first: _distance, smaller = closer (squared L2 under the default metric)
    /// </summary>
    /// <param name="queryEmbedding">
    /// The question as meaning — its embedding, produced by the retrieval pipeline with the
    /// SAME model that embedded the stored memories. Catches "Sergio moved to Norway" for
    /// "where does Sergio live": zero shared words, same meaning.
    /// </param>
    /// <param name="tags">Every requested tag must be present on a memory for it to surface.</param>
    /// <param name="options">The Lance vector-search knobs; null = the validated defaults.</param>
    /// <param name="ct">Cancels the read.</param>
    public async IAsyncEnumerable<MemoryHit> SearchAsync(
        float[] queryEmbedding,
        IReadOnlyCollection<Contracts.Tag> tags,
        VectorSearchOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        options ??= new();

        // The candidate pool must at least cover the requested page.
        var k = Math.Max(options.K, options.Limit);

        // Tag containment is NOT pushed down into the search: with tag filters the pool must cover
        // the whole table, or matching rows outside the top-k would be silently missed (the
        // validated oversample rule).
        if (tags.Count > 0)
            k = Math.Max(k, await CountMemoriesAsync(ct).ConfigureAwait(false));

        // Every value is bound as a named $parameter — the Lance named arguments included
        // (validated live 2026-07-20: k := $k, prefilter := $prefilter, … all bind). Only two
        // things can never be parameters and stay in the text: the FLOAT[N] dimension (a type,
        // not a value) and the optional knob CLAUSES, appended only when the caller set them so
        // the engine's defaults stay intact — their values are still bound.
        var nprobs   = options.Nprobs is not null ? ", nprobs := $nprobs" : "";
        var useIndex = options.UseIndex is not null ? ", use_index := $use_index" : "";

        var commandText =
            $"""
             SELECT memory_id,
                    memory_type,
                    content,
                    importance,
                    sentiment,
                    urgency,
                    tags,
                    evidence,
                    supersedes,
                    validity_start,
                    validity_end,
                    retained_at,
                    last_accessed_at,
                    retracted_at,
                    superseded_at,
                    superseded_by,
                    _distance
             FROM lance_vector_search('ldb.main.memories', 'embedding', CAST($query_embedding AS FLOAT[{queryEmbedding.Length}]),
                                      k := $k,
                                      prefilter := $prefilter,
                                      refine_factor := $refine_factor{nprobs}{useIndex})
             WHERE is_retracted = false
               AND is_superseded = false
               AND array_has_all(tags, CAST($tags AS VARCHAR[]))
             ORDER BY _distance ASC
             LIMIT $limit
             """;

        var tagValues = tags.Select(MemoryRecordMapper.EncodeTag).ToList();

        var hits = await connections.ExecuteAsync(
                connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = commandText;
                    command.Parameters.Add(new("query_embedding", queryEmbedding));
                    command.Parameters.Add(new("k", k));
                    command.Parameters.Add(new("prefilter", options.Prefilter));
                    command.Parameters.Add(new("refine_factor", options.RefineFactor));
                    command.Parameters.Add(new("tags", tagValues));
                    command.Parameters.Add(new("limit", options.Limit));

                    if (options.Nprobs is { } probes)
                        command.Parameters.Add(new("nprobs", probes));

                    if (options.UseIndex is { } index)
                        command.Parameters.Add(new("use_index", index));

                    var       results = new List<MemoryHit>();
                    using var reader  = command.ExecuteReader();

                    while (reader.Read()) {
                        // The distance arrives as a single-precision FLOAT; Convert widens it (a
                        // strict GetDouble would throw on a Single). Only the vector leg ran —
                        // the other scores are null by construction, never fabricated.
                        results.Add(new(
                            ReadStoredMemory(reader),
                            HybridScore: null,
                            VectorDistance: Convert.ToDouble(reader.GetValue(16), CultureInfo.InvariantCulture),
                            KeywordScore: null));
                    }

                    return results;
                }, ct)
            .ConfigureAwait(false);

        foreach (var hit in hits)
            yield return hit;
    }

    /// <summary>
    /// Full-text search: ranks memories by keyword relevance (BM25) alone.
    ///
    /// Recall's view of the world:
    /// - retracted and superseded memories never surface
    /// - every requested tag must be present
    /// - scores are BM25 _score: larger = better, corpus-relative — a gate, not a calibrated threshold
    /// </summary>
    /// <param name="query">
    /// The question as plain words — matched literally (BM25) against memory content.
    /// Not SQL, no operators: a bag of words, e.g. "where does Sergio live".
    /// Exact-token gold: names, ids, error codes — the things embeddings blur.
    /// </param>
    /// <param name="tags">Every requested tag must be present on a memory for it to surface.</param>
    /// <param name="options">The Lance full-text-search knobs; null = the validated defaults.</param>
    /// <param name="ct">Cancels the read.</param>
    public async IAsyncEnumerable<MemoryHit> SearchAsync(
        string query,
        IReadOnlyCollection<Contracts.Tag> tags,
        FullTextSearchOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        options ??= new();

        // The candidate pool must at least cover the requested page.
        var k = Math.Max(options.K, options.Limit);

        // Tag containment is NOT pushed down into the search: with tag filters the pool must cover
        // the whole table, or matching rows outside the top-k would be silently missed (the
        // validated oversample rule).
        if (tags.Count > 0)
            k = Math.Max(k, await CountMemoriesAsync(ct).ConfigureAwait(false));

        // Every value is bound as a named $parameter — the Lance named arguments included
        // (validated live 2026-07-20). Nothing varies in the text, so it is a true const.
        const string commandText =
            """
            SELECT memory_id,
                   memory_type,
                   content,
                   importance,
                   sentiment,
                   urgency,
                   tags,
                   evidence,
                   supersedes,
                   validity_start,
                   validity_end,
                   retained_at,
                   last_accessed_at,
                   retracted_at,
                   superseded_at,
                   superseded_by,
                   _score
            FROM lance_fts('ldb.main.memories', 'content', $query,
                           k := $k,
                           prefilter := $prefilter)
            WHERE is_retracted = false
              AND is_superseded = false
              AND array_has_all(tags, CAST($tags AS VARCHAR[]))
            ORDER BY _score DESC
            LIMIT $limit
            """;

        var tagValues = tags.Select(MemoryRecordMapper.EncodeTag).ToList();

        var hits = await connections.ExecuteAsync(
                connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = commandText;
                    command.Parameters.Add(new("query", query));
                    command.Parameters.Add(new("k", k));
                    command.Parameters.Add(new("prefilter", options.Prefilter));
                    command.Parameters.Add(new("tags", tagValues));
                    command.Parameters.Add(new("limit", options.Limit));

                    var       results = new List<MemoryHit>();
                    using var reader  = command.ExecuteReader();

                    while (reader.Read()) {
                        // The score arrives as a single-precision FLOAT; Convert widens it (a
                        // strict GetDouble would throw on a Single). Only the keyword leg ran —
                        // the other scores are null by construction, never fabricated.
                        results.Add(new(
                            ReadStoredMemory(reader),
                            HybridScore: null,
                            VectorDistance: null,
                            KeywordScore: Convert.ToDouble(reader.GetValue(16), CultureInfo.InvariantCulture)));
                    }

                    return results;
                }, ct)
            .ConfigureAwait(false);

        foreach (var hit in hits)
            yield return hit;
    }

    /// <summary>
    /// Hybrid search: blends vector similarity and keyword relevance (BM25) for the query.
    ///
    /// Recall's view of the world:
    /// - retracted and superseded memories never surface
    /// - every requested tag must be present
    /// - scores are Lance's _hybrid_score: larger = better, corpus-relative — a gate, not a calibrated threshold
    ///
    /// The caller supplies the query in both shapes: the text (keyword leg) and its embedding
    /// (vector leg) — embedding the query belongs to the retrieval pipeline, not to storage.
    /// </summary>
    /// <param name="query">
    /// The question as plain words — matched literally (BM25) against memory content.
    /// Not SQL, no operators: a bag of words, e.g. "where does Sergio live".
    /// Exact-token gold: names, ids, error codes — the things embeddings blur.
    /// </param>
    /// <param name="queryEmbedding">
    /// The same question as meaning — its embedding, produced by the retrieval pipeline with the
    /// SAME model that embedded the stored memories. Catches "Sergio moved to Norway" for the
    /// question above: zero shared words, same meaning.
    /// </param>
    /// <param name="tags">Every requested tag must be present on a memory for it to surface.</param>
    /// <param name="options">The Lance hybrid-search knobs; null = the validated defaults.</param>
    /// <param name="ct">Cancels the read.</param>
    public async IAsyncEnumerable<MemoryHit> SearchAsync(
        string query,
        float[] queryEmbedding,
        IReadOnlyCollection<Contracts.Tag> tags,
        HybridSearchOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        options ??= new();

        // The candidate pool must at least cover the requested page.
        var k = Math.Max(options.K, options.Limit);

        // Tag containment is NOT pushed down into the search: with tag filters the pool must cover
        // the whole table, or matching rows outside the top-k would be silently missed (the
        // validated oversample rule).
        if (tags.Count > 0)
            k = Math.Max(k, await CountMemoriesAsync(ct).ConfigureAwait(false));

        // Every value is bound as a named $parameter — the Lance named arguments included
        // (validated live 2026-07-20: k := $k, alpha := $alpha, … all bind). Only two things can
        // never be parameters and stay in the text: the FLOAT[N] dimension (a type, not a value)
        // and the optional knob CLAUSES, appended only when the caller set them so the engine's
        // defaults stay intact — their values are still bound.
        var nprobs   = options.Nprobs is not null ? ", nprobs := $nprobs" : "";
        var useIndex = options.UseIndex is not null ? ", use_index := $use_index" : "";

        var commandText =
            $"""
             SELECT memory_id,
                    memory_type,
                    content,
                    importance,
                    sentiment,
                    urgency,
                    tags,
                    evidence,
                    supersedes,
                    validity_start,
                    validity_end,
                    retained_at,
                    last_accessed_at,
                    retracted_at,
                    superseded_at,
                    superseded_by,
                    _distance,
                    _score,
                    _hybrid_score
             FROM lance_hybrid_search('ldb.main.memories', 'embedding', CAST($query_embedding AS FLOAT[{queryEmbedding.Length}]),
                                      'content', $query,
                                      k := $k,
                                      prefilter := $prefilter,
                                      alpha := $alpha,
                                      refine_factor := $refine_factor,
                                      oversample_factor := $oversample_factor{nprobs}{useIndex})
             WHERE is_retracted = false
               AND is_superseded = false
               AND array_has_all(tags, CAST($tags AS VARCHAR[]))
             ORDER BY _hybrid_score DESC
             LIMIT $limit
             """;

        var tagValues = tags.Select(MemoryRecordMapper.EncodeTag).ToList();

        var hits = await connections.ExecuteAsync(
                connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = commandText;
                    command.Parameters.Add(new("query_embedding", queryEmbedding));
                    command.Parameters.Add(new("query", query));
                    command.Parameters.Add(new("k", k));
                    command.Parameters.Add(new("prefilter", options.Prefilter));
                    command.Parameters.Add(new("alpha", options.Alpha));
                    command.Parameters.Add(new("refine_factor", options.RefineFactor));
                    command.Parameters.Add(new("oversample_factor", options.OversampleFactor));
                    command.Parameters.Add(new("tags", tagValues));
                    command.Parameters.Add(new("limit", options.Limit));

                    if (options.Nprobs is { } probes)
                        command.Parameters.Add(new("nprobs", probes));

                    if (options.UseIndex is { } index)
                        command.Parameters.Add(new("use_index", index));

                    var       results = new List<MemoryHit>();
                    using var reader  = command.ExecuteReader();

                    while (reader.Read()) {
                        // The scores arrive as single-precision FLOATs; Convert widens them (a
                        // strict GetDouble would throw on a Single). A leg that did not surface
                        // the row leaves its diagnostic column NULL — only the blend is always set.
                        results.Add(new(
                            ReadStoredMemory(reader),
                            HybridScore: Convert.ToDouble(reader.GetValue(18), CultureInfo.InvariantCulture),
                            VectorDistance: reader.IsDBNull(16) ? null : Convert.ToDouble(reader.GetValue(16), CultureInfo.InvariantCulture),
                            KeywordScore: reader.IsDBNull(17) ? null : Convert.ToDouble(reader.GetValue(17), CultureInfo.InvariantCulture)));
                    }

                    return results;
                }, ct)
            .ConfigureAwait(false);

        foreach (var hit in hits)
            yield return hit;
    }

    // How many rows the memories table holds right now — the tag-filter oversample bound.
    Task<int> CountMemoriesAsync(CancellationToken ct) {
        const string sql = "SELECT count(*) FROM ldb.main.memories";

        return connections.ExecuteAsync(
            connection => {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            }, ct);
    }

    /// <summary>The stored memory with the given id, or null. Never hides: retracted and superseded memories come back too.</summary>
    public async ValueTask<Contracts.StoredMemory?> GetAsync(string memoryId, CancellationToken ct = default) => 
        await GetAsync([memoryId], ct).FirstOrDefaultAsync(ct);

    /// <summary>Streams the stored memories for the given ids; ids that don't exist are simply absent. Never hides.</summary>
    public async IAsyncEnumerable<Contracts.StoredMemory> GetAsync(string[] memoryIds, [EnumeratorCancellation] CancellationToken ct = default) {
        const string sql =
            """
            SELECT memory_id,
                   memory_type,
                   content,
                   importance,
                   sentiment,
                   urgency,
                   tags,
                   evidence,
                   supersedes,
                   validity_start,
                   validity_end,
                   retained_at,
                   last_accessed_at,
                   retracted_at,
                   superseded_at,
                   superseded_by
            FROM ldb.main.memories
            """;

        var single = memoryIds.Length == 1;

        // Two possible WHERE clauses:
        // - one id   => equality: pushed down into the lance scan (a point lookup)
        // - many ids => containment: NOT pushed down, evaluated above a full scan
        var commandText = single
            ? $"{sql}\nWHERE memory_id = $memory_id"
            : $"{sql}\nWHERE array_contains(CAST($memory_ids AS VARCHAR[]), memory_id)";

        var memories = await connections.ExecuteAsync(
                connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = commandText;
                    command.Parameters.Add(single
                        ? new DuckDBParameter("memory_id", memoryIds[0])
                        : new DuckDBParameter("memory_ids", memoryIds));

                    return ReadAllStoredMemories(command);
                }, ct)
            .ConfigureAwait(false);

        foreach (var memory in memories)
            yield return memory;
    }

    /// <summary>
    /// Streams the whole supersession family that contains the given memory — every memory
    /// connected to it through the supersession chain — oldest retained first (memory_id
    /// settles exact ties, so the order is total). A missing id streams nothing. Never hides:
    /// most of a family IS superseded — that is the point of reading its history.
    ///
    /// Why one recursive query is enough (no graph machinery):
    /// - the edges live in ONE column: superseded_by is a single value, so a memory has at
    ///   most one successor — a family is a tree with one living head, never a general graph
    /// - the walk runs on that column in BOTH directions with plain equality (my successor:
    ///   memory_id = my superseded_by; my predecessors: superseded_by = my memory_id) — the
    ///   supersedes ARRAYS are never consulted, so array containment never enters the query
    /// - UNION (not UNION ALL) makes the recursion set-based: a row already in the family is
    ///   never expanded twice, and the recursion stops when no new row appears
    /// </summary>
    public async IAsyncEnumerable<Contracts.StoredMemory> GetLineageAsync(string memoryId, [EnumeratorCancellation] CancellationToken ct = default) {
        // One statement, three parts:
        // - the seed: the requested memory
        // - the recursive step: everything ONE supersession hop away from the family so far,
        //   in either direction
        // - the final SELECT: the family's full rows, oldest first
        const string sql =
            """
            WITH RECURSIVE family AS (
                SELECT memory_id,
                       superseded_by
                FROM ldb.main.memories
                WHERE memory_id = $memory_id
              UNION
                SELECT m.memory_id,
                       m.superseded_by
                FROM ldb.main.memories AS m, family AS f
                WHERE m.memory_id = f.superseded_by
                   OR m.superseded_by = f.memory_id
            )
            SELECT memory_id,
                   memory_type,
                   content,
                   importance,
                   sentiment,
                   urgency,
                   tags,
                   evidence,
                   supersedes,
                   validity_start,
                   validity_end,
                   retained_at,
                   last_accessed_at,
                   retracted_at,
                   superseded_at,
                   superseded_by
            FROM ldb.main.memories
            WHERE memory_id IN (SELECT memory_id FROM family)
            ORDER BY retained_at,
                     memory_id
            """;

        var memories = await connections.ExecuteAsync(
                connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = sql;
                    command.Parameters.Add(new("memory_id", memoryId));

                    return ReadAllStoredMemories(command);
                }, ct)
            .ConfigureAwait(false);

        foreach (var memory in memories)
            yield return memory;
    }

    /// <summary>
    /// Lists memories by tags (ALL must be present) and types (any of), sorted and limited.
    /// Retracted memories never come back; superseded ones stay readable history.
    ///
    /// Ordering guarantees:
    /// - the order is always total — two identical calls return rows in the same order
    /// - sorting by importance breaks its ties by last access, in the same direction:
    ///   descending = most important, most recently used first; ascending = least important,
    ///   longest untouched first (the eviction sweep)
    /// </summary>
    public async IAsyncEnumerable<Contracts.StoredMemory> ListAsync(
        IReadOnlyCollection<Contracts.Tag> tags,
        IReadOnlyCollection<Contracts.MemoryType> types,
        Contracts.RecollectSort sort,
        Contracts.SortDirection direction,
        int limit,
        [EnumeratorCancellation] CancellationToken ct = default
    ) {
        // One TRUE-CONST statement serves every filter, sort, and direction combination.
        //
        // Filters:
        // - empty types list => the type filter is off (that is what the len() check does;
        //   $types is referenced twice but bound once — named parameters allow that)
        // - empty tags list  => array_has_all is always true, so no tag filter
        //
        // Ordering — three terms, most significant first, plus one trick to stay const:
        // - the trick: ASC/DESC are SQL keywords, not values, so they can never be bound as
        //   parameters. Every sort term is a NUMBER multiplied by $direction (1 = ascending,
        //   -1 = descending) — negating numbers and sorting ascending IS sorting descending.
        //   That is also why the first CASE coerces every key to DOUBLE: only numbers sign-flip.
        // - term 1: the caller's key, picked by name in the CASE; epoch_ms turns the timestamps
        //   into plain numbers so all three keys compare the same way.
        // - term 2: the tie-break, ONLY for importance — for the other keys the second CASE
        //   yields the constant 0, which orders nothing. Importance has a handful of levels, so
        //   ties are the norm and something must order the rows inside a tie group; the
        //   microsecond timestamps never tie, so they need no such term. It multiplies by
        //   $direction too, so both intents read naturally: descending = most important, most
        //   recently used first; ascending = least important, longest untouched first — the
        //   eviction sweep. It is last_accessed_at, not retained_at, because staleness is about
        //   when a memory was last USED, not when it was formed.
        // - term 3: memory_id, which never ties — every result has exactly ONE valid order.
        //   Without it, rows inside a tie group come back in whatever order the engine likes,
        //   and two identical calls can disagree.
        const string sql =
            """
            SELECT memory_id,
                   memory_type,
                   content,
                   importance,
                   sentiment,
                   urgency,
                   tags,
                   evidence,
                   supersedes,
                   validity_start,
                   validity_end,
                   retained_at,
                   last_accessed_at,
                   retracted_at,
                   superseded_at,
                   superseded_by
            FROM ldb.main.memories
            WHERE is_retracted = false
              AND (len(CAST($types AS INTEGER[])) = 0 OR array_contains(CAST($types AS INTEGER[]), memory_type))
              AND array_has_all(tags, CAST($tags AS VARCHAR[]))
            ORDER BY (CASE $sort_key
                        WHEN 'importance'       THEN importance::DOUBLE
                        WHEN 'last_accessed_at' THEN epoch_ms(last_accessed_at)::DOUBLE
                        ELSE epoch_ms(retained_at)::DOUBLE
                      END) * $direction,
                     (CASE WHEN $sort_key = 'importance' THEN epoch_ms(last_accessed_at)::DOUBLE ELSE 0 END) * $direction,
                     memory_id
            LIMIT $limit
            """;

        var typeValues = types.Distinct().Select(type => (int)type).ToList();
        var tagValues  = tags.Select(MemoryRecordMapper.EncodeTag).ToList();

        var sortKey = sort switch {
            Contracts.RecollectSort.LastAccessedAt => "last_accessed_at",
            Contracts.RecollectSort.Importance     => "importance",
            _                                      => "retained_at", // default: when the memory was recorded
        };

        // The number every sort term is multiplied by: 1 keeps the natural (ascending) order,
        // -1 flips it to descending.
        var sortSign = direction == Contracts.SortDirection.Ascending ? 1 : -1;

        var memories = await connections.ExecuteAsync(
                connection => {
                    using var command = connection.CreateCommand();
                    command.CommandText = sql;
                    command.Parameters.Add(new("types", typeValues));
                    command.Parameters.Add(new("tags", tagValues));
                    command.Parameters.Add(new("sort_key", sortKey));
                    command.Parameters.Add(new("direction", sortSign));
                    command.Parameters.Add(new("limit", limit));

                    return ReadAllStoredMemories(command);
                }, ct)
            .ConfigureAwait(false);

        foreach (var memory in memories)
            yield return memory;
    }

    static List<Contracts.StoredMemory> ReadAllStoredMemories(DbCommand command) {
        var memories = new List<Contracts.StoredMemory>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
            memories.Add(ReadStoredMemory(reader));

        return memories;
    }

    // Reads one row POSITIONALLY, in the SELECT column order, off the validated wire shapes (KB):
    // - VARCHAR[] arrives as List<string>
    // - a populated BLOB arrives as a Stream, an empty one as byte[]
    // - TIMESTAMPTZ arrives as DateTimeOffset (a bare DateTime clock reading means UTC)
    static Contracts.StoredMemory ReadStoredMemory(DbDataReader reader) {
        var stored = new Contracts.StoredMemory {
            MemoryId       = reader.GetString(0),
            MemoryType     = (Contracts.MemoryType)reader.GetInt32(1),
            Content        = reader.GetString(2),
            Importance     = (Contracts.MemoryImportance)reader.GetInt32(3),
            Sentiment      = (Contracts.MemorySentiment)reader.GetInt32(4),
            Urgency        = (Contracts.MemoryUrgency)reader.GetInt32(5),
            RetainedAt     = Timestamp.FromDateTimeOffset(Utc(reader, 11)),
            LastAccessedAt = Timestamp.FromDateTimeOffset(Utc(reader, 12)),
            SupersededBy   = reader.GetString(15),
        };

        stored.Tags.AddRange(((IEnumerable<string>)reader.GetValue(6)).Select(MemoryRecordMapper.DecodeTag));
        stored.Supersedes.AddRange((IEnumerable<string>)reader.GetValue(8));

        var evidence = ReadBlob(reader, 7);

        if (evidence.Length > 0)
            stored.Evidence = Contracts.Evidence.Parser.ParseFrom(evidence);

        if (!reader.IsDBNull(9)) {
            stored.Validity = new() { PerceivedStart = Timestamp.FromDateTimeOffset(Utc(reader, 9)) };

            if (!reader.IsDBNull(10))
                stored.Validity.PerceivedEnd = Timestamp.FromDateTimeOffset(Utc(reader, 10));
        }

        if (!reader.IsDBNull(13))
            stored.RetractedAt = Timestamp.FromDateTimeOffset(Utc(reader, 13));

        if (!reader.IsDBNull(14))
            stored.SupersededAt = Timestamp.FromDateTimeOffset(Utc(reader, 14));

        return stored;

        // Timestamp wire shapes:
        // - normally a DateTimeOffset — use it as-is
        // - some driver paths hand back a bare DateTime — its clock reading means UTC
        static DateTimeOffset Utc(DbDataReader reader, int ordinal) =>
            reader.GetValue(ordinal) switch {
                DateTimeOffset instant => instant,
                DateTime clockReading  => new(DateTime.SpecifyKind(clockReading, DateTimeKind.Unspecified), TimeSpan.Zero),
                var other              => throw new NotSupportedException($"Unsupported timestamp value of type '{other.GetType()}'."),
            };

        // BLOB wire shapes differ by content:
        // - populated => a Stream (UnmanagedMemoryStream) — read it out fully and dispose it
        // - empty     => byte[]
        static byte[] ReadBlob(DbDataReader reader, int ordinal) {
            switch (reader.GetValue(ordinal)) {
                case byte[] bytes: return bytes;

                case Stream stream:
                    try {
                        using var memory = new MemoryStream();
                        stream.CopyTo(memory);
                        return memory.ToArray();
                    } finally {
                        stream.Dispose();
                    }

                case var other: throw new NotSupportedException($"Unsupported BLOB value of type '{other.GetType()}'.");
            }
        }
    }

}
