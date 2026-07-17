// Live validation of duckdb-connector-spec.md §15 + open spikes, against DuckDB.NET.Data.Full 1.5.3 (osx_arm64).
using System.Globalization;
using DuckDB.NET.Data;

var results = new List<(string Name, bool Ok, string Detail)>();
var storeDir = Path.GetFullPath("lancestore");
if (Directory.Exists(storeDir)) Directory.Delete(storeDir, recursive: true);
var datasetPath = Path.Combine(storeDir, "vs_docs.lance");

using var conn = new DuckDBConnection("DataSource=:memory:");
conn.Open();

void Exec(string sql, params object?[] ps)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    foreach (var p in ps) cmd.Parameters.Add(new DuckDBParameter(p));
    cmd.ExecuteNonQuery();
}

List<Dictionary<string, object?>> Query(string sql, params object?[] ps)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    foreach (var p in ps) cmd.Parameters.Add(new DuckDBParameter(p));
    using var reader = cmd.ExecuteReader();
    var rows = new List<Dictionary<string, object?>>();
    while (reader.Read())
    {
        var row = new Dictionary<string, object?>();
        for (var i = 0; i < reader.FieldCount; i++)
            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        rows.Add(row);
    }
    return rows;
}

void Check(string name, Func<string> body)
{
    try
    {
        var detail = body();
        results.Add((name, true, detail));
        Console.WriteLine($"  PASS  {name}  {detail}");
    }
    catch (Exception e)
    {
        var msg = e.Message.Split('\n')[0];
        results.Add((name, false, msg));
        Console.WriteLine($"  FAIL  {name}  [{e.GetType().Name}] {msg}");
    }
}

string Fmt(object? v) => v switch
{
    null => "null",
    double d => d.ToString("0.####", CultureInfo.InvariantCulture),
    float f => f.ToString("0.####", CultureInfo.InvariantCulture),
    System.Collections.IEnumerable en and not string =>
        "[" + string.Join(",", en.Cast<object?>().Select(Fmt)) + "]",
    _ => v.ToString()!
};

Console.WriteLine("== 1. Extension bootstrap ==");
Check("install-load-lance", () =>
{
    Exec("INSTALL lance; LOAD lance;");
    var v = Query("SELECT extension_version FROM duckdb_extensions() WHERE extension_name = 'lance'");
    return $"version={Fmt(v.FirstOrDefault()?.GetValueOrDefault("extension_version"))}";
});
Check("duckdb-version", () => $"duckdb={Query("SELECT version() AS v")[0]["v"]}");

Console.WriteLine("== 2. ATTACH on a non-existent StoragePath (§15 open question) ==");
Check("attach-missing-dir", () =>
{
    Exec($"ATTACH '{storeDir}' AS ns (TYPE lance)");
    return $"dirCreatedByAttach={Directory.Exists(storeDir)}";
});

Console.WriteLine("== 3. CREATE TABLE incl. LIST(VARCHAR) tags column ==");
Check("create-table", () =>
{
    Exec("CREATE TABLE ns.main.vs_docs (id VARCHAR, category VARCHAR, tags VARCHAR[], content VARCHAR, vec FLOAT[4])");
    return "columns: id VARCHAR, category VARCHAR, tags VARCHAR[] (LIST), content VARCHAR, vec FLOAT[4] (ARRAY)";
});
Check("dataset-path-on-disk", () =>
    Directory.Exists(datasetPath) || File.Exists(datasetPath)
        ? $"exists: {datasetPath}"
        : throw new InvalidOperationException($"no dataset at {datasetPath}; storeDir contents: {string.Join(",", Directory.GetFileSystemEntries(storeDir).Select(Path.GetFileName))}"));

Console.WriteLine("== 4. Writes: literal INSERT, parameterized INSERT, bulk, MERGE upsert ==");
Check("insert-literal", () =>
{
    Exec("""
        INSERT INTO ns.main.vs_docs VALUES
        ('k1', 'cat-a', ['x','y'], 'the quick brown fox jumps', [1.0, 0.0, 0.0, 0.0]::FLOAT[4]),
        ('k2', 'cat-b', ['y','z'], 'the lazy dog sleeps',       [0.0, 1.0, 0.0, 0.0]::FLOAT[4]),
        ('k3', 'cat-a', ['z'],     'quack goes the duck',       [-1.0, 0.0, 0.0, 0.0]::FLOAT[4])
        """);
    return "3 known rows (e1, e2, -e1)";
});
Check("insert-parameterized-vector", () =>
{
    Exec("INSERT INTO ns.main.vs_docs VALUES (?, ?, CAST(? AS VARCHAR[]), ?, CAST(? AS FLOAT[4]))",
        "k4", "cat-b", new List<string> { "x", "z" }, "parameterized row", new List<float> { 0.5f, 0.5f, 0.5f, 0.5f });
    return "bound List<string> tags + List<float> vec via CAST(? AS ...)";
});
Check("bulk-insert-2000", () =>
{
    Exec("""
        INSERT INTO ns.main.vs_docs
        SELECT 'r' || i,
               CASE WHEN i % 2 = 0 THEN 'cat-a' ELSE 'cat-b' END,
               ['t' || (i % 5)::VARCHAR],
               'filler document ' || i,
               [0.1 + random() * 0.8, 0.1 + random() * 0.8, 0.1 + random() * 0.8, 0.1 + random() * 0.8]::FLOAT[4]
        FROM range(2000) t(i)
        """);
    return $"count={Query("SELECT count(*) AS c FROM ns.main.vs_docs")[0]["c"]}";
});
Check("merge-upsert-update-existing", () =>
{
    Exec("""
        MERGE INTO ns.main.vs_docs AS t
        USING (SELECT ? AS id, ? AS category, CAST(? AS VARCHAR[]) AS tags, ? AS content, CAST(? AS FLOAT[4]) AS vec) AS s
        ON t.id = s.id
        WHEN MATCHED THEN UPDATE SET category = s.category, tags = s.tags, content = s.content, vec = s.vec
        WHEN NOT MATCHED THEN INSERT (id, category, tags, content, vec) VALUES (s.id, s.category, s.tags, s.content, s.vec)
        """,
        "k1", "cat-a", new List<string> { "x", "y", "updated" }, "the quick brown fox jumps again", new List<float> { 1f, 0f, 0f, 0f });
    var tags = Query("SELECT tags FROM ns.main.vs_docs WHERE id = 'k1'")[0]["tags"];
    return $"k1.tags now {Fmt(tags)}";
});
Check("merge-upsert-insert-new", () =>
{
    Exec("""
        MERGE INTO ns.main.vs_docs AS t
        USING (SELECT ? AS id, ? AS category, CAST(? AS VARCHAR[]) AS tags, ? AS content, CAST(? AS FLOAT[4]) AS vec) AS s
        ON t.id = s.id
        WHEN MATCHED THEN UPDATE SET category = s.category, tags = s.tags, content = s.content, vec = s.vec
        WHEN NOT MATCHED THEN INSERT (id, category, tags, content, vec) VALUES (s.id, s.category, s.tags, s.content, s.vec)
        """,
        "k5", "cat-a", new List<string> { "w" }, "brand new row via merge", new List<float> { 0f, 0f, 1f, 0f });
    return $"exists={Query("SELECT count(*) AS c FROM ns.main.vs_docs WHERE id = 'k5'")[0]["c"]}";
});

Console.WriteLine("== 5. Reads: CLR types coming back ==");
Check("read-back-clr-types", () =>
{
    var row = Query("SELECT vec, tags FROM ns.main.vs_docs WHERE id = 'k1'")[0];
    return $"vec is {row["vec"]!.GetType().Name}, tags is {row["tags"]!.GetType().Name}";
});

Console.WriteLine("== 6. Vector search: literal vs prepared-parameter query vector (open spike) ==");
Check("vector-search-literal", () =>
{
    var rows = Query($"SELECT id, _distance FROM lance_vector_search('{datasetPath}', 'vec', [1.0, 0.0, 0.0, 0.0]::FLOAT[4], k := 3) ORDER BY _distance");
    return string.Join(" ", rows.Select(r => $"{r["id"]}:{Fmt(r["_distance"])}"));
});
Check("vector-search-param-bound", () =>
{
    var rows = Query($"SELECT id, _distance FROM lance_vector_search('{datasetPath}', 'vec', CAST(? AS FLOAT[4]), k := 3) ORDER BY _distance",
        new List<float> { 1f, 0f, 0f, 0f });
    return string.Join(" ", rows.Select(r => $"{r["id"]}:{Fmt(r["_distance"])}"));
});
Check("vector-search-via-attached-name", () =>
{
    var rows = Query("SELECT id, _distance FROM lance_vector_search('ns.main.vs_docs', 'vec', [1.0, 0.0, 0.0, 0.0]::FLOAT[4], k := 1)");
    return string.Join(" ", rows.Select(r => $"{r["id"]}:{Fmt(r["_distance"])}"));
});

Console.WriteLine("== 7. Distance semantics without index (expect squared L2 brute force) ==");
Check("distance-l2-noindex", () =>
{
    var rows = Query($"SELECT id, _distance FROM lance_vector_search('{datasetPath}', 'vec', [1.0, 0.0, 0.0, 0.0]::FLOAT[4], k := 2500)")
        .Where(r => new[] { "k1", "k2", "k3" }.Contains((string)r["id"]!)).ToDictionary(r => (string)r["id"]!, r => r["_distance"]);
    return $"identical(k1)={Fmt(rows.GetValueOrDefault("k1"))} orthogonal(k2)={Fmt(rows.GetValueOrDefault("k2"))} opposite(k3)={Fmt(rows.GetValueOrDefault("k3"))} (squared L2 ⇒ 0 / 2 / 4)";
});

Console.WriteLine("== 8. Index DDL against raw path (dual addressing) ==");
Check("create-vector-index-cosine", () =>
{
    Exec($"CREATE INDEX vec_idx ON '{datasetPath}' (vec) USING IVF_FLAT WITH (metric_type = 'cosine', num_partitions = 1)");
    return "IVF_FLAT cosine, num_partitions=1";
});
Check("distance-cosine-with-index", () =>
{
    var rows = Query($"SELECT id, _distance FROM lance_vector_search('{datasetPath}', 'vec', [1.0, 0.0, 0.0, 0.0]::FLOAT[4], k := 2500, use_index := true)")
        .Where(r => new[] { "k1", "k2", "k3" }.Contains((string)r["id"]!)).ToDictionary(r => (string)r["id"]!, r => r["_distance"]);
    return $"identical(k1)={Fmt(rows.GetValueOrDefault("k1"))} orthogonal(k2)={Fmt(rows.GetValueOrDefault("k2"))} opposite(k3)={Fmt(rows.GetValueOrDefault("k3"))} (1-cos ⇒ 0 / 1 / 2)";
});
Check("drop-index", () =>
{
    Exec($"DROP INDEX vec_idx ON '{datasetPath}'");
    return $"indexes left={Query($"SHOW INDEXES ON '{datasetPath}'").Count}";
});
Check("create-index-ivf-pq", () =>
{
    Exec($"CREATE INDEX vec_idx ON '{datasetPath}' (vec) USING IVF_PQ WITH (metric_type = 'cosine', num_partitions = 1, num_sub_vectors = 2, num_bits = 8)");
    return "IVF_PQ cosine (spec: QuantizedFlat/Dynamic mapping), num_sub_vectors=2 on dim 4";
});
Check("distance-cosine-ivf-pq", () =>
{
    var rows = Query($"SELECT id, _distance FROM lance_vector_search('{datasetPath}', 'vec', [1.0, 0.0, 0.0, 0.0]::FLOAT[4], k := 2500, use_index := true, refine_factor := 4)")
        .Where(r => new[] { "k1", "k2", "k3" }.Contains((string)r["id"]!)).ToDictionary(r => (string)r["id"]!, r => r["_distance"]);
    return $"identical(k1)={Fmt(rows.GetValueOrDefault("k1"))} orthogonal(k2)={Fmt(rows.GetValueOrDefault("k2"))} opposite(k3)={Fmt(rows.GetValueOrDefault("k3"))} (PQ-approximate 1-cos)";
});
Check("recreate-index-ivf-hnsw-pq", () =>
{
    Exec($"DROP INDEX vec_idx ON '{datasetPath}'");
    Exec($"CREATE INDEX vec_idx ON '{datasetPath}' (vec) USING IVF_HNSW_PQ WITH (metric_type = 'cosine', num_partitions = 1, num_sub_vectors = 2, num_bits = 8, hnsw_m = 16, hnsw_ef_construction = 100)");
    var idx = Query($"SHOW INDEXES ON '{datasetPath}'").First(r => r.Values.Any(v => v?.ToString() == "vec_idx"));
    return "IVF_HNSW_PQ cosine (spec: Hnsw mapping): " + string.Join(" ", idx.Select(kv => $"{kv.Key}={Fmt(kv.Value)}"));
});
Check("distance-cosine-ivf-hnsw-pq", () =>
{
    var rows = Query($"SELECT id, _distance FROM lance_vector_search('{datasetPath}', 'vec', [1.0, 0.0, 0.0, 0.0]::FLOAT[4], k := 2500, use_index := true, refine_factor := 4)")
        .Where(r => new[] { "k1", "k2", "k3" }.Contains((string)r["id"]!)).ToDictionary(r => (string)r["id"]!, r => r["_distance"]);
    return $"identical(k1)={Fmt(rows.GetValueOrDefault("k1"))} orthogonal(k2)={Fmt(rows.GetValueOrDefault("k2"))} opposite(k3)={Fmt(rows.GetValueOrDefault("k3"))} (HNSW+PQ approximate 1-cos)";
});
Check("hnsw-pq-top1-correct", () =>
{
    var rows = Query($"SELECT id FROM lance_vector_search('{datasetPath}', 'vec', [1.0, 0.0, 0.0, 0.0]::FLOAT[4], k := 1, use_index := true, refine_factor := 4)");
    return (string)rows[0]["id"]! == "k1" ? "top-1 = k1 (exact match found through HNSW_PQ)" : throw new InvalidOperationException($"top-1 was {rows[0]["id"]}");
});
Check("create-fts-index", () =>
{
    Exec($"CREATE INDEX fts_idx ON '{datasetPath}' (content) USING INVERTED");
    return "INVERTED on content";
});
Check("create-labellist-index", () =>
{
    Exec($"CREATE INDEX tags_idx ON '{datasetPath}' (tags) USING LABEL_LIST");
    return "LABEL_LIST on tags";
});
Check("create-btree-index", () =>
{
    Exec($"CREATE INDEX cat_idx ON '{datasetPath}' (category) USING BTREE");
    return "BTREE on category";
});
Check("show-indexes-shape", () =>
{
    var rows = Query($"SHOW INDEXES ON '{datasetPath}'");
    var cols = string.Join(",", rows[0].Keys);
    return $"{rows.Count} indexes; columns: {cols}";
});

Console.WriteLine("== 9. FTS + hybrid search ==");
Check("fts-search", () =>
{
    var rows = Query($"SELECT id, _score FROM lance_fts('{datasetPath}', 'content', 'fox', k := 5)");
    return string.Join(" ", rows.Select(r => $"{r["id"]}:{Fmt(r["_score"])}"));
});
Check("hybrid-search", () =>
{
    var rows = Query($"SELECT id, _hybrid_score, _distance, _score FROM lance_hybrid_search('{datasetPath}', 'vec', CAST(? AS FLOAT[4]), 'content', 'fox', k := 5, alpha := 0.5) ORDER BY _hybrid_score DESC",
        new List<float> { 1f, 0f, 0f, 0f });
    return string.Join(" ", rows.Select(r => $"{r["id"]}:h={Fmt(r["_hybrid_score"])}"));
});

Console.WriteLine("== 10. Filters via WHERE clause (pushdown into the table function?) ==");
Check("filter-where-prefilter-true", () =>
{
    var rows = Query($"SELECT id, category FROM lance_vector_search('{datasetPath}', 'vec', [1.0, 0.0, 0.0, 0.0]::FLOAT[4], k := 5, prefilter := true) WHERE category = 'cat-a'");
    var cats = rows.Select(r => (string)r["category"]!).Distinct().ToList();
    return $"rows={rows.Count} (k=5; 5 ⇒ prefilter semantics, <5 ⇒ post-filter) cats={string.Join(",", cats)}";
});
Check("filter-where-prefilter-false", () =>
{
    var rows = Query($"SELECT id, category FROM lance_vector_search('{datasetPath}', 'vec', [1.0, 0.0, 0.0, 0.0]::FLOAT[4], k := 5, prefilter := false) WHERE category = 'cat-a'");
    return $"rows={rows.Count} (k=5)";
});
Check("filter-where-param-bound-prefilter", () =>
{
    var rows = Query($"SELECT id, category FROM lance_vector_search('{datasetPath}', 'vec', [1.0, 0.0, 0.0, 0.0]::FLOAT[4], k := 5, prefilter := true) WHERE category = ?", "cat-a");
    return $"rows={rows.Count} (5 ⇒ bound-param filter still pushed down as prefilter)";
});
Check("filter-where-hybrid", () =>
{
    var rows = Query($"SELECT id FROM lance_hybrid_search('{datasetPath}', 'vec', CAST(? AS FLOAT[4]), 'content', 'document', k := 5, prefilter := true) WHERE category = ?",
        new List<float> { 1f, 0f, 0f, 0f }, "cat-a");
    return $"rows={rows.Count}: {string.Join(",", rows.Select(r => r["id"]))}";
});
Check("filter-where-tags-array-has-any", () =>
{
    var rows = Query($"SELECT id, tags FROM lance_vector_search('{datasetPath}', 'vec', [1.0, 0.0, 0.0, 0.0]::FLOAT[4], k := 5, prefilter := true) WHERE array_has_any(tags, ['x'])");
    return $"rows={rows.Count}: " + string.Join(" ", rows.Select(r => $"{r["id"]}:{Fmt(r["tags"])}"));
});
Check("filter-where-fts", () =>
{
    var rows = Query($"SELECT id FROM lance_fts('{datasetPath}', 'content', 'document', k := 5, prefilter := true) WHERE category = 'cat-b'");
    return $"rows={rows.Count}: {string.Join(",", rows.Take(5).Select(r => r["id"]))}";
});
Check("explain-filter-pushdown", () =>
{
    var plan = Query($"EXPLAIN SELECT id FROM lance_vector_search('{datasetPath}', 'vec', [1.0, 0.0, 0.0, 0.0]::FLOAT[4], k := 5, prefilter := true) WHERE category = 'cat-a'");
    var text = string.Join("\n", plan.Select(r => string.Join(" ", r.Values.Select(Fmt))));
    var hasFilterNode = text.Contains("FILTER", StringComparison.OrdinalIgnoreCase);
    return $"plan has separate FILTER node={hasFilterNode} (false ⇒ fully pushed down)";
});

Console.WriteLine("== 11. Scheduler primitives: staleness detection + OPTIMIZE append ==");
Check("post-index-write-staleness", () =>
{
    Exec("""
        INSERT INTO ns.main.vs_docs
        SELECT 'p' || i, 'cat-post', ['post'], 'post-index doc ' || i,
               [0.1 + random() * 0.8, 0.1 + random() * 0.8, 0.1 + random() * 0.8, 0.1 + random() * 0.8]::FLOAT[4]
        FROM range(50) t(i)
        """);
    var idx = Query($"SHOW INDEXES ON '{datasetPath}'");
    var vecIdx = idx.First(r => r.Values.Any(v => v?.ToString() == "vec_idx"));
    return "vec_idx row: " + string.Join(" ", vecIdx.Select(kv => $"{kv.Key}={Fmt(kv.Value)}"));
});
Check("post-index-search-merges-unindexed", () =>
{
    var rows = Query($"SELECT id FROM lance_vector_search('{datasetPath}', 'vec', [1.0, 0.0, 0.0, 0.0]::FLOAT[4], k := 2500)");
    var postRows = rows.Count(r => ((string)r["id"]!).StartsWith('p'));
    return postRows > 0 ? $"unindexed rows present in results ({postRows}/50)" : throw new InvalidOperationException("no post-index rows returned");
});
Check("alter-index-optimize-append", () =>
{
    Exec($"ALTER INDEX vec_idx ON '{datasetPath}' OPTIMIZE WITH (mode = 'append')");
    var idx = Query($"SHOW INDEXES ON '{datasetPath}'");
    var vecIdx = idx.First(r => r.Values.Any(v => v?.ToString() == "vec_idx"));
    return "after append: " + string.Join(" ", vecIdx.Select(kv => $"{kv.Key}={Fmt(kv.Value)}"));
});
Check("prefilter-equality-outside-global-topk", () =>
{
    var rows = Query($"SELECT id FROM lance_vector_search('{datasetPath}', 'vec', [1.0, 0.0, 0.0, 0.0]::FLOAT[4], k := 5, prefilter := true) WHERE category = ?", "cat-post");
    return $"rows={rows.Count} (5 ⇒ equality truly prefilters; 0 ⇒ silent post-filter)";
});
Check("prefilter-tags-outside-global-topk", () =>
{
    var rows = Query($"SELECT id FROM lance_vector_search('{datasetPath}', 'vec', [1.0, 0.0, 0.0, 0.0]::FLOAT[4], k := 5, prefilter := true) WHERE array_has_any(tags, ['post'])");
    return $"rows={rows.Count} (5 ⇒ array_has_any truly prefilters; 0 ⇒ silent post-filter)";
});
Check("prefilter-tags-noindex-path", () =>
{
    var rows = Query($"SELECT id FROM lance_vector_search('{datasetPath}', 'vec', [1.0, 0.0, 0.0, 0.0]::FLOAT[4], k := 5, prefilter := true, use_index := false) WHERE array_has_any(tags, ['post'])");
    return $"rows={rows.Count} (use_index=false; 5 ⇒ flat path prefilters tags)";
});
Check("prefilter-tags-oversample-workaround", () =>
{
    var rows = Query($"SELECT id FROM lance_vector_search('{datasetPath}', 'vec', [1.0, 0.0, 0.0, 0.0]::FLOAT[4], k := 2500) WHERE array_has_any(tags, ['post']) LIMIT 5");
    return $"rows={rows.Count} (k=all + WHERE + LIMIT: correctness fallback)";
});
Check("alter-index-optimize-retrain", () =>
{
    Exec($"ALTER INDEX vec_idx ON '{datasetPath}' OPTIMIZE WITH (mode = 'retrain')");
    return "retrain ok";
});
Check("optimize-compaction", () =>
{
    Exec($"OPTIMIZE '{datasetPath}' WITH (materialize_deletions = true, materialize_deletions_threshold = 0.1)");
    return "compaction ok";
});
Check("vacuum-lance", () =>
{
    Exec($"VACUUM LANCE '{datasetPath}' WITH (older_than_seconds = 0, retain_n_versions = 1)");
    return "vacuum ok";
});

Console.WriteLine("== 11b. Attached-name search after OPTIMIZE/VACUUM (dataset-cache staleness) ==");
Check("search-attached-name-after-vacuum", () =>
{
    string RunIt() => string.Join(",", Query(
        "SELECT id FROM lance_vector_search('ns.main.vs_docs', 'vec', CAST(? AS FLOAT[4]), k := 5, prefilter := true) WHERE category = ?",
        new List<float> { 1f, 0f, 0f, 0f }, "cat-a").Select(r => r["id"]));
    try { return $"no stale cache (first try): {RunIt()}"; }
    catch
    {
        try { return $"stale on first try; plain retry recovered: {RunIt()}"; }
        catch
        {
            try
            {
                Exec("DETACH ns");
                Exec($"ATTACH '{storeDir}' AS ns (TYPE lance)");
                return $"stale; recovered via DETACH+ATTACH: {RunIt()}";
            }
            catch
            {
                try
                {
                    // lance_delete.cpp invalidates the table-keyed cache entry only.
                    Exec("DELETE FROM ns.main.vs_docs WHERE id = ?", "cache-sentinel");
                    return $"recovered via row-DELETE invalidation: {RunIt()}";
                }
                catch
                {
                    // lance_index.cpp invalidates BOTH table- and path-keyed entries.
                    Exec($"DROP INDEX cat_idx ON '{datasetPath}'");
                    Exec($"CREATE INDEX cat_idx ON '{datasetPath}' (category) USING BTREE");
                    return $"retry/DETACH/DELETE all stale; recovered via index-DDL invalidation: {RunIt()}";
                }
            }
        }
    }
});
Check("search-raw-path-after-vacuum", () =>
{
    string RunIt() => string.Join(",", Query(
        $"SELECT id FROM lance_vector_search('{datasetPath}', 'vec', CAST(? AS FLOAT[4]), k := 3)",
        new List<float> { 1f, 0f, 0f, 0f }).Select(r => r["id"]));
    try { return $"raw path fine after vacuum: {RunIt()}"; }
    catch
    {
        try { return $"raw path stale once, retry recovered: {RunIt()}"; }
        catch { return "raw path stale and NOT recoverable by retry in-session (fresh connection required)"; }
    }
});

Console.WriteLine("== 11c. Direct-to-Lance storage proof (no DuckDB-side storage) ==");
Check("fresh-connection-reattach", () =>
{
    using var conn2 = new DuckDBConnection("DataSource=:memory:");
    conn2.Open();
    using var cmd = conn2.CreateCommand();
    cmd.CommandText = $"LOAD lance; ATTACH '{storeDir}' AS ns2 (TYPE lance);";
    cmd.ExecuteNonQuery();
    cmd.CommandText = "SELECT count(*) AS c FROM ns2.main.vs_docs";
    using var r1 = cmd.ExecuteReader();
    r1.Read();
    var count = r1.GetInt64(0);
    r1.Close();
    cmd.CommandText = "SELECT id FROM lance_vector_search('ns2.main.vs_docs', 'vec', [1.0, 0.0, 0.0, 0.0]::FLOAT[4], k := 1)";
    using var r2 = cmd.ExecuteReader();
    r2.Read();
    var top = r2.GetString(0);
    return $"brand-new in-memory connection sees count={count}, top-1 search={top} — all state lives in the .lance dir";
});
Check("storage-artifacts-lance-only", () =>
{
    var entries = Directory.GetFileSystemEntries(storeDir, "*", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).ToList();
    var duckFiles = Directory.GetFiles(storeDir, "*.duckdb*", SearchOption.AllDirectories)
        .Concat(Directory.GetFiles(Directory.GetCurrentDirectory(), "*.duckdb*", SearchOption.TopDirectoryOnly)).ToList();
    var lanceInternals = Directory.GetFileSystemEntries(datasetPath).Select(Path.GetFileName).ToList();
    return duckFiles.Count == 0
        ? $"storeDir=[{string.Join(",", entries)}] dataset internals=[{string.Join(",", lanceInternals)}] duckdbFiles=0"
        : throw new InvalidOperationException($"unexpected duckdb artifacts: {string.Join(",", duckFiles)}");
});

Console.WriteLine("== 12. Row-level UPDATE / DELETE, collection listing, DROP ==");
Check("update-row", () =>
{
    Exec("UPDATE ns.main.vs_docs SET category = ? WHERE id = ?", "cat-z", "k2");
    return $"k2.category={Query("SELECT category FROM ns.main.vs_docs WHERE id = 'k2'")[0]["category"]}";
});
Check("delete-row", () =>
{
    Exec("DELETE FROM ns.main.vs_docs WHERE id = ?", "k3");
    return $"k3 remaining={Query("SELECT count(*) AS c FROM ns.main.vs_docs WHERE id = 'k3'")[0]["c"]}";
});
Check("delete-missing-key-no-throw", () =>
{
    Exec("DELETE FROM ns.main.vs_docs WHERE id = ?", "no-such-key");
    return "no exception";
});
Check("list-collections", () =>
{
    var rows = Query("SELECT table_name FROM duckdb_tables() WHERE database_name = 'ns'");
    return string.Join(",", rows.Select(r => r["table_name"]));
});
Check("drop-table", () =>
{
    Exec("DROP TABLE ns.main.vs_docs");
    var rows = Query("SELECT table_name FROM duckdb_tables() WHERE database_name = 'ns'");
    var onDisk = Directory.Exists(datasetPath) || File.Exists(datasetPath);
    return $"tablesLeft=[{string.Join(",", rows.Select(r => r["table_name"]))}] datasetStillOnDisk={onDisk}";
});
Check("detach", () =>
{
    Exec("DETACH ns");
    return "ok";
});

Console.WriteLine();
var failed = results.Count(r => !r.Ok);
Console.WriteLine($"== SUMMARY: {results.Count - failed}/{results.Count} passed, {failed} failed ==");
foreach (var (name, ok, detail) in results.Where(r => !r.Ok))
    Console.WriteLine($"  FAILED: {name} — {detail}");
