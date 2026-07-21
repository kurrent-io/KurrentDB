// Copyright (c) Kurrent, Inc and/or licensed to Kurrent, Inc under one or more agreements.
// Kurrent, Inc licenses this file to you under the Kurrent License v1 (see LICENSE.md).

using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using DuckDB.NET.Data;
using Kurrent.Kontext.Mcp.Model;
using Kurrent.Quack;
using Kurrent.Quack.ConnectionPool;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace Kurrent.Kontext.Storage;

/// <summary>
/// Self-contained memory store over one Lance directory. Thread-safe; one instance per storage path
/// per process (DuckDB holds a single-process lock on its engine file). Dispose checkpoints and closes.
/// </summary>
public sealed class KontextStore : IDisposable {
    const string Alias        = "kx";                 // attach alias — must differ from the engine-file stem
    const string EngineFile   = "kontext_engine.ddb"; // stem "kontext_engine" ≠ alias "kx" (else ATTACH IF NOT EXISTS no-ops → data loss)
    const string Table        = "memories";
    const string Qualified    = $"{Alias}.main.{Table}";
    const char   TagSeparator = '\u001F'; // unit separator: encodes Tag as "scope<US>value" for LABEL_LIST

    // Column list shared by every read; vec is deliberately excluded from reads.
    const string Columns =
        "id, type, content, importance, sentiment, urgency, evidence, tags, supersedes, " +
        "valid_from, valid_to, retained_at, last_accessed_at, retracted, retracted_at, superseded_at, superseded_by";

    static readonly int[] s_conflictBackoffMs = [20, 50];

    readonly string                                        _datasetPath;
    readonly int                                           _dimensions;
    readonly IEmbeddingGenerator<string, Embedding<float>> _embedder;
    readonly DuckDBConnectionPool                          _pool;
    volatile bool                                          _disposed;
    volatile bool                                          _everExecuted;
    volatile bool                                          _vectorIndexCreated;

    public KontextStore(string storagePath, IEmbeddingGenerator<string, Embedding<float>> embedder, int dimensions) {
        ArgumentException.ThrowIfNullOrEmpty(storagePath);
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentOutOfRangeException.ThrowIfLessThan(dimensions, 2);

        var fullPath = Path.GetFullPath(storagePath);
        _datasetPath = Path.Combine(fullPath, $"{Table}.lance");
        _embedder    = embedder;
        _dimensions  = dimensions;

        // Kurrent.Quack's DuckDBConnectionPool used directly: a private LanceConnectionPool subclass bootstraps each
        // physical connection (INSTALL/LOAD lance, UTC session, ATTACH). The pool hands out a DuckDBAdvancedConnection
        // whose CreateCommand() is the same ADO.NET surface a raw DuckDBConnection exposes — so the DuckDB command
        // code below is unchanged. The engine file is stable (never deleted); DuckDB's file lock enforces single-
        // process ownership of it.
        var connectionString = $"Data Source={Path.Combine(fullPath, EngineFile)};access_mode=READ_WRITE";
        Directory.CreateDirectory(fullPath);
        _pool = new LanceConnectionPool(connectionString, fullPath, Alias);
    }
    
    /// <summary>Creates the table and scalar/FTS/tag indexes if missing. The vector index comes lazily (≥256 rows).</summary>
    public Task EnsureCreatedAsync(CancellationToken ct = default) =>
        ExecuteAsync(
            conn => {
                Exec(
                    conn,
                    $"CREATE TABLE IF NOT EXISTS {Qualified} (id VARCHAR, type SMALLINT, content VARCHAR, importance SMALLINT, sentiment SMALLINT, urgency SMALLINT, evidence VARCHAR, tags VARCHAR[], supersedes VARCHAR[], valid_from TIMESTAMPTZ, valid_to TIMESTAMPTZ, retained_at TIMESTAMPTZ, last_accessed_at TIMESTAMPTZ, retracted BOOLEAN, retracted_at TIMESTAMPTZ, superseded_at TIMESTAMPTZ, superseded_by VARCHAR, vec FLOAT[{_dimensions}])");

                var existing = ShowIndexNames(conn);

                if (!existing.Contains("id_idx"))
                    Exec(conn, $"CREATE INDEX id_idx ON '{_datasetPath}' (id) USING BTREE");

                if (!existing.Contains("content_fts"))
                    Exec(conn, $"CREATE INDEX content_fts ON '{_datasetPath}' (content) USING INVERTED");

                if (!existing.Contains("tags_idx"))
                    Exec(conn, $"CREATE INDEX tags_idx ON '{_datasetPath}' (tags) USING LABEL_LIST");

                return 0;
            }, ct);
    
    /// <summary>
    /// Stores a memory (id generated when absent), returning its id and the most similar existing
    /// memories. When the memory supersedes others, those are stamped superseded by this one.
    /// </summary>
    public async Task<RetainedMemory> RetainAsync(Memory memory, int relatedTop = 3, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentException.ThrowIfNullOrEmpty(memory.Content);

        var id     = string.IsNullOrEmpty(memory.Id) ? $"mem-{Guid.NewGuid():N}" : memory.Id;
        var now    = DateTimeOffset.UtcNow;
        var vector = Normalize((await _embedder.GenerateAsync([memory.Content], cancellationToken: ct).ConfigureAwait(false))[0].Vector);

        var related = await ExecuteAsync(
                conn => {
                    // Related memories BEFORE the upsert (the new row must not shadow its own neighbors). Vectors are
                    // L2-normalized, so _distance is squared-L2 = 2 - 2*cos with OR without an index: sim = 1 - d/2.
                    var hits = new List<RelatedMemory>(relatedTop);

                    using (var search = conn.CreateCommand()) {
                        search.CommandText =
                            $"SELECT id, _distance FROM lance_vector_search('{Qualified}', 'vec', CAST(? AS FLOAT[{_dimensions}]), k := {relatedTop + 1}, prefilter := true, refine_factor := 4) WHERE retracted = ? AND id <> ? ORDER BY _distance LIMIT {relatedTop}";

                        search.Parameters.Add(new DuckDBParameter(vector));
                        search.Parameters.Add(new DuckDBParameter(false));
                        search.Parameters.Add(new DuckDBParameter(id));
                        using var reader = search.ExecuteReader();

                        while (reader.Read()) {
                            hits.Add(
                                new RelatedMemory {
                                    MemoryId = reader.GetString(0), Similarity = 1d - (Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture) / 2d),
                                });
                        }
                    }

                    using (var merge = conn.CreateCommand()) {
                        merge.CommandText =
                            $"MERGE INTO {Qualified} AS t USING (SELECT ? AS id, ? AS type, ? AS content, ? AS importance, ? AS sentiment, ? AS urgency, ? AS evidence, CAST(? AS VARCHAR[]) AS tags, CAST(? AS VARCHAR[]) AS supersedes, ? AS valid_from, ? AS valid_to, ? AS retained_at, ? AS last_accessed_at, ? AS retracted, ? AS retracted_at, ? AS superseded_at, ? AS superseded_by, CAST(? AS FLOAT[{_dimensions}]) AS vec) AS s ON t.id = s.id WHEN MATCHED THEN UPDATE SET type = s.type, content = s.content, importance = s.importance, sentiment = s.sentiment, urgency = s.urgency, evidence = s.evidence, tags = s.tags, supersedes = s.supersedes, valid_from = s.valid_from, valid_to = s.valid_to, retained_at = s.retained_at, last_accessed_at = s.last_accessed_at, retracted = s.retracted, retracted_at = s.retracted_at, superseded_at = s.superseded_at, superseded_by = s.superseded_by, vec = s.vec WHEN NOT MATCHED THEN INSERT (id, type, content, importance, sentiment, urgency, evidence, tags, supersedes, valid_from, valid_to, retained_at, last_accessed_at, retracted, retracted_at, superseded_at, superseded_by, vec) VALUES (s.id, s.type, s.content, s.importance, s.sentiment, s.urgency, s.evidence, s.tags, s.supersedes, s.valid_from, s.valid_to, s.retained_at, s.last_accessed_at, s.retracted, s.retracted_at, s.superseded_at, s.superseded_by, s.vec)";

                        merge.Parameters.Add(new DuckDBParameter(id));
                        merge.Parameters.Add(new DuckDBParameter((short)memory.Type));
                        merge.Parameters.Add(new DuckDBParameter(memory.Content));
                        merge.Parameters.Add(new DuckDBParameter((short)memory.Importance));
                        merge.Parameters.Add(new DuckDBParameter((short)memory.Sentiment));
                        merge.Parameters.Add(new DuckDBParameter((short)memory.Urgency));
                        merge.Parameters.Add(NullableParam(memory.Evidence is null ? null : JsonSerializer.Serialize(memory.Evidence)));
                        merge.Parameters.Add(new DuckDBParameter(memory.Tags.Select(EncodeTag).ToList()));
                        merge.Parameters.Add(new DuckDBParameter(memory.Supersedes.ToList()));
                        merge.Parameters.Add(NullableParam(memory.Validity?.From.UtcDateTime));
                        merge.Parameters.Add(NullableParam(memory.Validity?.To?.UtcDateTime));
                        merge.Parameters.Add(new DuckDBParameter(now.UtcDateTime));
                        merge.Parameters.Add(NullableParam((DateTime?)null)); // last_accessed_at
                        merge.Parameters.Add(new DuckDBParameter(false));     // retracted
                        merge.Parameters.Add(NullableParam((DateTime?)null)); // retracted_at
                        merge.Parameters.Add(NullableParam((DateTime?)null)); // superseded_at
                        merge.Parameters.Add(NullableParam((string?)null));   // superseded_by
                        merge.Parameters.Add(new DuckDBParameter(vector));
                        merge.ExecuteNonQuery();
                    }

                    // Per-id UPDATE (the validated predicate shape) to stamp superseded memories.
                    foreach (var supersededId in memory.Supersedes) {
                        using var stamp = conn.CreateCommand();
                        stamp.CommandText = $"UPDATE {Qualified} SET superseded_at = ?, superseded_by = ? WHERE id = ?";
                        stamp.Parameters.Add(new DuckDBParameter(now.UtcDateTime));
                        stamp.Parameters.Add(new DuckDBParameter(id));
                        stamp.Parameters.Add(new DuckDBParameter(supersededId));
                        stamp.ExecuteNonQuery();
                    }

                    return hits;
                }, ct)
            .ConfigureAwait(false);

        await TryCreateVectorIndexAsync(ct).ConfigureAwait(false);
        return new RetainedMemory { MemoryId = id, Related = related };
    }
    
    /// <summary>
    /// Hybrid recall: blends vector similarity and BM25 keyword relevance for <paramref name="query"/>,
    /// excluding retracted memories. Tag filters use the validated oversample path; MinScore filters on
    /// the blended score (larger = better). Lean or Full per <see cref="RecallOptions.IncludeFull"/>.
    /// </summary>
    public async Task<IReadOnlyList<RecalledMemory>> RecallAsync(string query, RecallOptions options, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrEmpty(query);
        ArgumentNullException.ThrowIfNull(options);
        var top = options.Limit > 0 ? options.Limit : 10;

        var vector = Normalize((await _embedder.GenerateAsync([query], cancellationToken: ct).ConfigureAwait(false))[0].Vector);

        return await ExecuteAsync(
                conn => {
                    // Tag containment is not pushed down by the extension: with tag filters the candidate pool
                    // must cover the table (validated oversample rule). Equality (retracted) prefilters fine.
                    var k     = options.Tags.Count > 0 ? Math.Max(Count(conn), top) : top;
                    var where = $" WHERE retracted = ?{(options.Tags.Count > 0 ? " AND array_has_any(tags, CAST(? AS VARCHAR[]))" : "")}";

                    using var cmd = conn.CreateCommand();

                    cmd.CommandText =
                        $"SELECT {Columns}, _hybrid_score FROM lance_hybrid_search('{Qualified}', 'vec', CAST(? AS FLOAT[{_dimensions}]), 'content', ?, k := {k}, prefilter := true, alpha := 0.5, refine_factor := 4){where} ORDER BY _hybrid_score DESC LIMIT {top}";

                    cmd.Parameters.Add(new DuckDBParameter(vector));
                    cmd.Parameters.Add(new DuckDBParameter(query));
                    cmd.Parameters.Add(new DuckDBParameter(false));

                    if (options.Tags.Count > 0)
                        cmd.Parameters.Add(new DuckDBParameter(options.Tags.Select(EncodeTag).ToList()));

                    var       hits   = new List<RecalledMemory>(top);
                    using var reader = cmd.ExecuteReader();

                    while (reader.Read()) {
                        var score = Convert.ToDouble(reader.GetValue(17), CultureInfo.InvariantCulture);

                        if (score < options.MinScore)
                            continue;

                        var stored = ReadStoredMemory(reader);

                        hits.Add(
                            options.IncludeFull
                                ? new RecalledMemory.Full { Score = score, Memory = stored }
                                : new RecalledMemory.Lean {
                                    Score = score,
                                    Memory = new LeanMemory {
                                        MemoryId   = stored.MemoryId,
                                        MemoryType = stored.MemoryType,
                                        Content    = stored.Content,
                                        Tags       = stored.Tags,
                                        Importance = stored.Importance,
                                        RetainedAt = stored.RetainedAt,
                                    },
                                });
                    }

                    return (IReadOnlyList<RecalledMemory>)hits;
                }, ct)
            .ConfigureAwait(false);
    }
    
    /// <summary>Lists non-retracted memories filtered by types/tags, sorted per the options. Plain SQL, no relevance.</summary>
    public Task<IReadOnlyList<StoredMemory>> RecollectAsync(RecollectOptions options, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(options);
        var top = options.Limit > 0 ? options.Limit : 50;

        var sortColumn = options.Sort switch {
            RecollectSort.LastAccessedAt => "last_accessed_at",
            RecollectSort.Importance     => "importance",
            _                            => "retained_at",
        };

        var direction = options.Direction == SortDirection.Ascending ? "ASC" : "DESC";

        return ExecuteAsync(
            conn => {
                var where = "WHERE retracted = ?";

                if (options.Types.Count > 0)
                    where += $" AND type IN ({string.Join(", ", Enumerable.Repeat("?", options.Types.Count))})";

                if (options.Tags.Count > 0)
                    where += " AND array_has_any(tags, CAST(? AS VARCHAR[]))";

                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT {Columns} FROM {Qualified} {where} ORDER BY {sortColumn} {direction} NULLS LAST LIMIT {top}";
                cmd.Parameters.Add(new DuckDBParameter(false));

                foreach (var type in options.Types)
                    cmd.Parameters.Add(new DuckDBParameter((short)type));

                if (options.Tags.Count > 0)
                    cmd.Parameters.Add(new DuckDBParameter(options.Tags.Select(EncodeTag).ToList()));

                var       results = new List<StoredMemory>(top);
                using var reader  = cmd.ExecuteReader();

                while (reader.Read())
                    results.Add(ReadStoredMemory(reader));

                return (IReadOnlyList<StoredMemory>)results;
            }, ct);
    }
    
    public Task<StoredMemory?> GetAsync(string id, CancellationToken ct = default) =>
        ExecuteAsync(
            conn => {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT {Columns} FROM {Qualified} WHERE id = ?";
                cmd.Parameters.Add(new DuckDBParameter(id));
                using var reader = cmd.ExecuteReader();
                return reader.Read() ? ReadStoredMemory(reader) : null;
            }, ct);

    /// <summary>Stamps <c>last_accessed_at</c> for the given memories (the caller decides when a recall counts as access).</summary>
    public Task MarkAccessedAsync(IReadOnlyList<string> ids, CancellationToken ct = default) =>
        ExecuteAsync(
            conn => {
                var now = DateTime.UtcNow;

                foreach (var id in ids) {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"UPDATE {Qualified} SET last_accessed_at = ? WHERE id = ?";
                    cmd.Parameters.Add(new DuckDBParameter(now));
                    cmd.Parameters.Add(new DuckDBParameter(id));
                    cmd.ExecuteNonQuery();
                }

                return 0;
            }, ct);

    /// <summary>Soft-retracts a memory (it stops appearing in recall/recollect; the row remains). Idempotent.</summary>
    public Task RetractAsync(string id, CancellationToken ct = default) =>
        ExecuteAsync(
            conn => {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"UPDATE {Qualified} SET retracted = ?, retracted_at = ? WHERE id = ?";
                cmd.Parameters.Add(new DuckDBParameter(true));
                cmd.Parameters.Add(new DuckDBParameter(DateTime.UtcNow));
                cmd.Parameters.Add(new DuckDBParameter(id));
                cmd.ExecuteNonQuery();
                return 0;
            }, ct);

    /// <summary>Folds unindexed rows into the vector index and compacts the dataset (two separate ops, validated).</summary>
    public async Task OptimizeAsync(CancellationToken ct = default) {
        await TryCreateVectorIndexAsync(ct).ConfigureAwait(false);

        await ExecuteAsync(
                conn => {
                    if (_vectorIndexCreated)
                        Exec(conn, $"ALTER INDEX vec_idx ON '{_datasetPath}' OPTIMIZE WITH (mode = 'append')");

                    Exec(conn, $"OPTIMIZE '{_datasetPath}' WITH (materialize_deletions = true, materialize_deletions_threshold = 0.1)");
                    return 0;
                }, ct)
            .ConfigureAwait(false);
    }

    Task TryCreateVectorIndexAsync(CancellationToken ct) {
        if (_vectorIndexCreated)
            return Task.CompletedTask;

        return ExecuteAsync(
            conn => {
                if (ShowIndexNames(conn).Contains("vec_idx")) {
                    _vectorIndexCreated = true;
                    return 0;
                }

                if (Count(conn) < 256)
                    return 0; // PQ training floor; brute-force search is correct meanwhile

                var nsv = Enumerable.Range(1, Math.Min(16, _dimensions)).Where(d => _dimensions % d == 0).Max();

                Exec(
                    conn,
                    $"CREATE INDEX vec_idx ON '{_datasetPath}' (vec) USING IVF_HNSW_PQ WITH (metric_type = 'l2', num_partitions = 1, num_sub_vectors = {nsv}, num_bits = 8, hnsw_m = 16, hnsw_ef_construction = 100)");

                _vectorIndexCreated = true;
                return 0;
            }, ct);
    }

    static string EncodeTag(Tag tag) => tag.Scope + TagSeparator + tag.Value;

    static Tag DecodeTag(string encoded) {
        var i = encoded.IndexOf(TagSeparator, StringComparison.Ordinal);
        return i < 0 ? new Tag { Value = encoded } : new Tag { Scope = encoded[..i], Value = encoded[(i + 1)..] };
    }

    static StoredMemory ReadStoredMemory(DbDataReader r) =>
        new() {
            MemoryId       = r.GetString(0),
            MemoryType     = (MemoryType)r.GetInt16(1),
            Content        = r.GetString(2),
            Importance     = (MemoryImportance)r.GetInt16(3),
            Sentiment      = (MemorySentiment)r.GetInt16(4),
            Urgency        = (MemoryUrgency)r.GetInt16(5),
            Evidence       = r.IsDBNull(6) ? null : JsonSerializer.Deserialize<Evidence>(r.GetString(6)),
            Tags           = [.. ((IEnumerable<string>)r.GetValue(7)).Select(DecodeTag)],
            Supersedes     = [.. (IEnumerable<string>)r.GetValue(8)],
            Validity       = r.IsDBNull(9) ? null : new TemporalContext { From = Utc(r, 9), To = r.IsDBNull(10) ? null : Utc(r, 10) },
            RetainedAt     = Utc(r, 11),
            LastAccessedAt = r.IsDBNull(12) ? null : Utc(r, 12),
            RetractedAt    = r.IsDBNull(14) ? null : Utc(r, 14),
            SupersededAt   = r.IsDBNull(15) ? null : Utc(r, 15),
            SupersededBy   = r.IsDBNull(16) ? null : r.GetString(16),
        };

    static DateTimeOffset Utc(DbDataReader r, int ordinal) => new(DateTime.SpecifyKind(r.GetDateTime(ordinal), DateTimeKind.Utc));

    static DuckDBParameter NullableParam(object? value) => new(value ?? DBNull.Value);

    /// <summary>L2-normalizes an embedding so squared-L2 distance equals 2 - 2*cosine — one similarity
    /// formula whether or not the vector index exists yet (pre-index brute force uses the l2 metric).</summary>
    static List<float> Normalize(ReadOnlyMemory<float> vector) {
        var array = vector.ToArray();
        var norm  = MathF.Sqrt(array.Sum(v => v * v));

        if (norm > 0f)
            for (var i = 0; i < array.Length; i++)
                array[i] /= norm;

        return [.. array];
    }

    // Runs a synchronous DuckDB operation against a pooled connection. The work runs INLINE on the caller's thread
    // (DuckDB has no true async API) and returns an already-completed task. Two validated transient failures are
    // handled: a "Retryable commit conflict" is retried on the SAME connection (×3 with backoff); a stale dataset
    // handle poisons the connection, which is disposed and recycled once on a fresh connection.
    Task<T> ExecuteAsync<T>(Func<DuckDBAdvancedConnection, T> operation, CancellationToken ct) {
        // Kurrent.Quack's Rent/Open have no disposed-guard of their own (a rent on a frozen pool silently mints a new
        // connection), so guard here; the recycle path re-checks before its second rent.
        ObjectDisposedException.ThrowIf(_disposed, this);

        try {
            ct.ThrowIfCancellationRequested();
            _everExecuted = true; // a physical connection is about to be opened → the dispose-time checkpoint is worth doing

            for (var isFreshRetry = false;; isFreshRetry = true) {
                // NEVER copy the Scope: its Dispose is the pool's only (liveness-unchecked) TryReturn caller, so
                // disposing two copies would re-admit the same not-thread-safe connection twice.
                var scope        = _pool.Rent(out var conn);
                var returnToPool = true;

                try {
                    for (var attempt = 0;; attempt++) {
                        try {
                            return Task.FromResult(operation(conn));
                        } catch (Exception ex) when (attempt < s_conflictBackoffMs.Length && IsRetryableCommitConflict(ex)) {
                            // Lance itself instructs the retry; the connection is healthy. Blocks the caller's thread.
                            Thread.Sleep(s_conflictBackoffMs[attempt]);
                        }
                    }
                } catch (Exception ex) when (IsStaleDatasetHandle(ex)) {
                    // Poisoned connection (dead cached dataset view): dispose it and DROP the Scope un-disposed so the
                    // pool's liveness-unchecked TryReturn can never re-admit it. The pool keeps no outstanding
                    // accounting, so dropping the Scope costs nothing.
                    returnToPool = false;
                    conn.Dispose();

                    if (isFreshRetry)
                        throw;

                    // About to mint a fresh connection (its Initialize re-ATTACHes, opening a current dataset view):
                    // re-check disposal so an operation racing Dispose does not rent from a frozen pool.
                    ObjectDisposedException.ThrowIf(_disposed, this);
                } finally {
                    if (returnToPool)
                        ((IDisposable)scope).Dispose();
                }
            }
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            return Task.FromCanceled<T>(ct);
        } catch (Exception ex) {
            return Task.FromException<T>(ex);
        }

        // Lance's optimistic-concurrency rejection ("... Retryable commit conflict for version N ... Please retry."):
        // transient, connection healthy, safe to re-run on the same connection.
        static bool IsRetryableCommitConflict(Exception ex) => ex.ToString().Contains("Retryable commit conflict", StringComparison.Ordinal);

        // A dead cached dataset view, in either validated shape: the vacuum/rewrite shape ("LanceError(IO)" + "Not
        // found") or the concurrent-writer shape ("... belongs to non-existent fragment ..."). Only a fresh
        // connection (re-ATTACH) converges.
        static bool IsStaleDatasetHandle(Exception ex) {
            var text = ex.ToString();

            return (text.Contains("LanceError(IO)", StringComparison.Ordinal) && text.Contains("Not found", StringComparison.Ordinal))
                || text.Contains("belongs to non-existent fragment", StringComparison.Ordinal);
        }
    }

    HashSet<string> ShowIndexNames(DuckDBAdvancedConnection conn) {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SHOW INDEXES ON '{_datasetPath}'";
        using var reader = cmd.ExecuteReader();
        var       names  = new HashSet<string>(StringComparer.Ordinal);

        while (reader.Read())
            names.Add(reader.GetString(0));

        return names;
    }

    static int Count(DuckDBAdvancedConnection conn) {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT count(*) FROM {Qualified}";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    static void Exec(DuckDBAdvancedConnection conn, string sql) {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Best-effort checkpoints the engine (flushing its WAL) before freezing and disposing the pool. The stable engine
    /// file and its <c>.wal</c> are durable state and are never deleted. Idempotent.
    /// </summary>
    public void Dispose() {
        if (_disposed)
            return;

        _disposed = true;

        // Flush the WAL into the engine file so it is left consistent on disk. Only worth doing if a connection was
        // ever opened (otherwise there is nothing to flush and opening one just to checkpoint would needlessly
        // materialize the engine file). Best-effort — the WAL is durable and DuckDB replays it on next open.
        if (_everExecuted) {
            try {
                using (_pool.Rent(out var conn)) {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "CHECKPOINT;";
                    cmd.ExecuteNonQuery();
                }
            } catch {
                // best-effort checkpoint
            }
        }

        // Disposing the pool freezes it and disposes every idle pooled connection, releasing their handles on the
        // engine file. The file itself is intentionally left on disk.
        _pool.Dispose();
    }

    /// <summary>
    /// A <see cref="DuckDBConnectionPool"/> that loads the <c>lance</c> extension, pins the session time zone to UTC,
    /// and attaches the store's Lance namespace on every physical connection it opens.
    /// </summary>
    sealed class LanceConnectionPool(string connectionString, string storagePath, string storageAlias)
        : DuckDBConnectionPool(connectionString) {
        // Paths are interpolated into SQL, so single quotes are doubled; the alias is a fixed bare identifier ("kx").
        // SET TimeZone='UTC' makes TIMESTAMPTZ read/write symmetric so UTC instants round-trip exactly. ATTACH IF NOT
        // EXISTS is idempotent per shared engine instance (a bare ATTACH fails on the second connection). Never DETACH.
        readonly string _initSql =
            $"INSTALL lance; LOAD lance; SET TimeZone='UTC'; ATTACH IF NOT EXISTS '{storagePath.Replace("'", "''", StringComparison.Ordinal)}' AS {storageAlias} (TYPE LANCE);";

        protected override void Initialize(DuckDBAdvancedConnection connection) {
            try {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = _initSql;
                cmd.ExecuteNonQuery();
            } catch {
                // Kurrent.Quack's Open() does not dispose the freshly-opened physical connection when Initialize
                // throws, which would leak an open native handle and keep the engine file locked. Dispose it here.
                connection.Dispose();
                throw;
            }
        }
    }
}