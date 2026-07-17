# 🦆⚡ DuckLance — can we build our search on DuckDB + Lance?

**Validation report & decision log** — written to be readable by everyone: engineers new to the topic, and product folks deciding whether to bet on this.

| | |
|---|---|
| **The question** | Can DuckDB + the Lance extension replace our hand-made Lucene + usearch search stack, wrapped as a standard Semantic Kernel connector? |
| **The answer** | ✅ **Yes.** Everything we need works, and we verified it by *running it*, not just reading docs. Three gotchas found; all three have simple, tested workarounds. |
| **Date** | 2026-07-17 |
| **How we verified** | 56 automated checks against a live session **+** an audit of the extension's actual source code ([lance-format/lance-duckdb](https://github.com/lance-format/lance-duckdb)) |
| **Environment** | macOS (Apple Silicon), .NET 10, `DuckDB.NET.Data.Full` 1.5.3 (bundles DuckDB v1.5.3), Lance extension build `533e0ee` via `INSTALL lance` |

---

## 1. What is this about?

Today, search in our product is **hand-made from three parts**:

- **Lucene** — keyword search (find documents containing "invoice")
- **usearch** — vector search (find documents that *mean* something similar, using AI embeddings)
- **Glue code we wrote ourselves** — keeps both indexes in sync on every write, merges two result lists into one ranking, handles persistence and crashes

Every part of that glue is a place where bugs live. Two indexes that must never disagree, hand-tuned score merging, custom recovery logic.

**The idea:** replace all of it with one embedded engine.

- **DuckDB** is a small, fast SQL database that runs *inside* our process — think "SQLite, but built for analytics". No server to deploy.
- **Lance** is a modern open file format designed for AI data: vectors, text, and tags in one table, with its own built-in indexes and versioned, transactional storage.
- The **Lance extension for DuckDB** (an official core extension, built by DuckDB + LanceDB together) makes Lance files behave like normal SQL tables — and adds three ready-made search functions: vector search, full-text search, and **hybrid search that combines both in a single call**.

We wrap this in a connector called **DuckLance** that implements Microsoft's standard vector-store API (`Microsoft.Extensions.VectorData`, used by Semantic Kernel). The rest of our codebase then talks to a standard interface and never knows what's underneath.

**One storage layer. One query for hybrid search. Real transactions. Zero servers.**

---

## 2. What we need vs. what it can do

| What we need | Verdict | Evidence |
|---|---|---|
| Store records with vectors + text + tags together | ✅ | One table: `FLOAT[768]` vector column, text column, `VARCHAR[]` tags column — created and used live |
| Write directly (insert / update / delete / **upsert**), no double-write | ✅ | Full SQL DML works on attached Lance tables, including `MERGE INTO` (a real one-statement upsert). Proven that data lands **only** in Lance files: we used an *in-memory* DuckDB, opened a brand-new connection, and everything was there; the data folder contains only Lance files, zero DuckDB files |
| Semantic (vector) search, fast at scale | ✅ | `lance_vector_search` with a real ANN index (`IVF_HNSW_PQ` — the quantized HNSW we planned to use) — built, searched, maintained live. One caveat: see Gotcha #1 |
| Keyword (full-text) search | ✅ | `INVERTED` index + `lance_fts`, BM25-style scoring — validated |
| **Hybrid** search (vector + keywords, one ranked list) | ✅ | `lance_hybrid_search` — one SQL call, tunable `alpha` (0 = pure text, 1 = pure vector). No hand-written score merging, ever again |
| Filter by category / metadata during search | ✅ | SQL `WHERE category = ?` with `prefilter := true` — properly filters *before* top-k selection, even for matches far from the query vector. Works with bound parameters (no SQL injection surface) and in hybrid search |
| Filter by tags (list contains) during search | ⚠️ works, with a workaround | The extension doesn't push tag-containment filters into the index yet. Validated workaround is simple and correct (Gotcha #2) |
| Runs embedded, no extra infrastructure | ✅ | In-process DuckDB + files on disk. `INSTALL lance; LOAD lance;` is the entire setup (one-time download, can be preloaded for air-gapped machines) |
| Safe under concurrent writes | ✅ | Lance has real MVCC/ACID transactions. Stress-tested: 120/120 concurrent upserts to the *same row* from 8 threads — zero errors, zero lost updates |
| Search stays correct as data grows/changes | ✅ | New writes are **never missing** from results (unindexed rows are brute-force scanned and merged automatically); indexes are refreshed with one SQL statement — our small background scheduler automates it |
| .NET support | ✅ | Standard ADO.NET driver (`DuckDB.NET`), parameterized queries throughout — including binding the query vector itself as a parameter |
| Platforms | ⚠️ | Linux x64/arm64, macOS Apple Silicon, Windows x64. **No Intel Mac** — worth a conscious sign-off |

---

## 3. How it works (the 60-second version)

```
┌────────────────────────── our process ──────────────────────────┐
│                                                                 │
│  Semantic Kernel / our code                                     │
│        │  standard VectorData API                               │
│        ▼                                                        │
│  DuckLance connector (C#)                                       │
│        │  SQL over ADO.NET (DuckDB.NET)                         │
│        ▼                                                        │
│  DuckDB engine + Lance extension        ← no server, in-process │
│        │  reads/writes/commits                                  │
└────────┼────────────────────────────────────────────────────────┘
         ▼
   /data/vectors/            ← just a folder
     └── docs.lance/         ← one Lance dataset per collection
         ├── data/               (the rows)
         ├── _indices/           (vector + text + tag indexes)
         └── _versions/          (transaction history — MVCC)
```

- A "collection" = one Lance dataset (a folder). The store = one parent folder, attached to DuckDB with a single `ATTACH` statement.
- Every write is an atomic Lance commit. DuckDB stores nothing itself — it's purely the engine.
- Bonus: Lance is an open format. The same files can be read by Python, Rust, Spark, etc. — no lock-in.

---

## 4. The three gotchas (each one: what happens → why → what we do)

Real findings from the live run. None are blockers; all three fixes are validated.

### Gotcha #1 — compressed indexes give wrong answers at small k, unless you ask for refinement

Our index of choice (`IVF_HNSW_PQ`) **compresses** vectors to be fast and memory-cheap ("PQ" = product quantization). Compression loses precision. In the live test, asking for the **1** nearest neighbor of a vector *identical* to a stored row returned **the wrong row**.

**Fix:** pass `refine_factor := 4` — the engine fetches 4× the candidates using compressed math, then re-ranks them with exact math. With it, results and distances were exact in every check.
**Our rule:** the connector *always* sets `refine_factor` when a quantized index is in play. Never optional.

### Gotcha #2 — tag filters aren't accelerated by the extension (yet)

Two ways to filter a search:
- **Prefilter** (good): first keep only matching rows, then find the top-k among them → you always get k results.
- **Post-filter** (bad): find the global top-k first, then drop non-matching ones → you can get fewer, or zero, results.

Equality filters (`WHERE category = ?`) genuinely **prefilter** — validated even when every matching row was far from the query vector. But tag containment (`WHERE array_has_any(tags, [...])`) is **not translated** by the extension's filter layer (checked in its source: `lance_filter_ir.cpp` has no entry for it), so it silently post-filters — we measured 2 results where 5 existed.

**Fix (validated correct):** *oversample* — ask for a large k, let DuckDB itself apply the tag filter, then `LIMIT`. Slightly more work per query, exact results.
**Later:** the extension is growing a `filter := '...'` parameter that reaches Lance's own filter engine (where tag filters *are* index-accelerated). When it ships for local data, we switch and delete the workaround.

### Gotcha #3 — storage cleanup can break searches in already-open connections

Lance keeps old versions of the data (that's what makes readers never block writers). `VACUUM` deletes old versions to reclaim disk. The extension caches an opened dataset handle per connection — and **maintenance commands never refresh that cache** (confirmed in source). If vacuum is configured too aggressively, a cached handle can point at files that no longer exist → searches in that connection start failing with `IO: Not found`.

We reproduced this deliberately (vacuum set to "delete everything old immediately") and mapped the recovery: reconnecting always fixes it; within a session, only an index-DDL statement clears the cache.

**Fix (policy, not code):** vacuum retention stays at **days**, never minutes — then a cached handle always finds its files and the failure is unreachable. Plus, defense in depth: the connection pool treats that specific error as "recycle the connection and retry".

### Small print (quick facts worth knowing)

- Syntax: inside `WITH (...)` use `=`, not `:=` (`:=` is only for search-function arguments). The official docs were inconsistent; we validated which form works.
- Lance's "L2" distance is **squared** Euclidean — which happens to map perfectly onto the standard API's `EuclideanSquaredDistance`. Cosine distance maps 1:1. No weird conversions needed.
- Hybrid search has **no** filter parameter of its own (checked in source) — filtering is done with a normal SQL `WHERE`, which we validated works there too.
- DuckDB has no true async I/O — the driver's async methods are sync-under-the-hood. Fine for us; just honest.
- First `INSTALL lance` downloads the extension (one-time, cached). For locked-down environments we support loading it from a local file.

---

## 5. What this replaces — the before/after

| Today (Lucene + usearch + glue) | With DuckLance |
|---|---|
| Two separate indexes that must be kept in sync by our code, on every write | One storage layer; one transactional write updates everything |
| Hand-written merging of keyword scores and vector scores into one ranking | `lance_hybrid_search(...)` — one call, tunable blend, maintained by DuckDB + LanceDB |
| Custom persistence, custom crash-recovery | Lance MVCC: every write is an atomic commit; stress-tested under maximum contention |
| Custom filter logic bolted onto two engines | Plain SQL `WHERE`, pushed down into the search |
| Our own index-rebuild choreography | One SQL statement (`ALTER INDEX ... OPTIMIZE`) + a small scheduler we own |
| Proprietary/self-invented on-disk layout | Open Lance format, readable from Python/Rust/Spark — and it's what LanceDB Cloud speaks, if we ever want a managed backend |

What stays ours: the connector code itself (mapping, the scheduler, the standard API surface) — a few small, testable components instead of a search engine.

---

## 6. Cheat-sheet: the actual SQL & C#

Everything below is copy-pasted from the validated test run (only renamed for clarity).

### One-time setup per store

```sql
INSTALL lance;                    -- one-time download (cached afterwards)
LOAD lance;
ATTACH '/data/vectors' AS vs (TYPE lance);   -- the folder; created lazily on first table
```

### Create a collection (+ its indexes)

```sql
CREATE TABLE IF NOT EXISTS vs.main.docs (
    id       VARCHAR,
    category VARCHAR,
    tags     VARCHAR[],          -- list of strings
    content  VARCHAR,
    vec      FLOAT[768]          -- fixed-size vector (dimension from the record definition)
);

-- Index DDL addresses the dataset by path:
CREATE INDEX vec_idx  ON '/data/vectors/docs.lance' (vec)
    USING IVF_HNSW_PQ WITH (metric_type = 'cosine', num_partitions = 256,
                            num_sub_vectors = 16, hnsw_m = 16, hnsw_ef_construction = 100);
CREATE INDEX fts_idx  ON '/data/vectors/docs.lance' (content) USING INVERTED;    -- full-text
CREATE INDEX tags_idx ON '/data/vectors/docs.lance' (tags)    USING LABEL_LIST;  -- tags
CREATE INDEX cat_idx  ON '/data/vectors/docs.lance' (category) USING BTREE;      -- equality filters
```

### Upsert (insert-or-update in one atomic statement)

```sql
MERGE INTO vs.main.docs AS t
USING (SELECT ? AS id, ? AS category, CAST(? AS VARCHAR[]) AS tags,
              ? AS content, CAST(? AS FLOAT[768]) AS vec) AS s
ON t.id = s.id
WHEN MATCHED     THEN UPDATE SET category = s.category, tags = s.tags,
                                 content = s.content, vec = s.vec
WHEN NOT MATCHED THEN INSERT (id, category, tags, content, vec)
                      VALUES (s.id, s.category, s.tags, s.content, s.vec);
```

```csharp
using var cmd = connection.CreateCommand();
cmd.CommandText = MergeSql;                                 // the statement above
cmd.Parameters.Add(new DuckDBParameter(record.Id));
cmd.Parameters.Add(new DuckDBParameter(record.Category));
cmd.Parameters.Add(new DuckDBParameter(record.Tags));       // List<string> → VARCHAR[] via CAST
cmd.Parameters.Add(new DuckDBParameter(record.Content));
cmd.Parameters.Add(new DuckDBParameter(embedding.ToArray()));// float[] → FLOAT[768] via CAST
await Task.Run(() => cmd.ExecuteNonQuery(), ct);
```

Everything is a **bound parameter** — including the vector. No string-building, no injection risk.

### Vector search, filtered by category

```sql
SELECT id, category, tags, content, _distance
FROM lance_vector_search('vs.main.docs', 'vec', CAST(? AS FLOAT[768]),
                         k := 12,                -- top + skip
                         prefilter := true,      -- filter BEFORE top-k (validated!)
                         refine_factor := 4)     -- Gotcha #1 fix: exact re-ranking
WHERE category = ?                               -- bound param, pushed into Lance
ORDER BY _distance                               -- smaller = closer
LIMIT 10 OFFSET 2;
```

### Hybrid search (vector + keywords, one ranked list)

```sql
SELECT id, category, content, _hybrid_score, _distance, _score
FROM lance_hybrid_search('vs.main.docs',
                         'vec',     CAST(? AS FLOAT[768]),   -- the semantic side
                         'content', ?,                       -- the keyword side
                         k := 10, prefilter := true,
                         alpha := 0.5,                       -- 0 = pure text … 1 = pure vector
                         refine_factor := 4)
WHERE category = ?
ORDER BY _hybrid_score DESC;                     -- larger = better
```

### Tag filter (the Gotcha #2 oversample workaround)

```sql
SELECT id, tags, _distance
FROM lance_vector_search('vs.main.docs', 'vec', CAST(? AS FLOAT[768]),
                         k := 400)               -- oversample: fetch a wide candidate pool
WHERE array_has_any(tags, [?])                   -- DuckDB applies this itself (exact)
ORDER BY _distance
LIMIT 10;
```

### Keep indexes fresh (what our scheduler runs)

```sql
SHOW INDEXES ON '/data/vectors/docs.lance';      -- rows_indexed vs COUNT(*) → how stale?
ALTER INDEX vec_idx ON '/data/vectors/docs.lance' OPTIMIZE WITH (mode = 'append');   -- fold in new rows
ALTER INDEX vec_idx ON '/data/vectors/docs.lance' OPTIMIZE WITH (mode = 'retrain');  -- periodic full rebuild
OPTIMIZE '/data/vectors/docs.lance' WITH (materialize_deletions = true);             -- compact
VACUUM LANCE '/data/vectors/docs.lance' WITH (older_than_seconds = 1209600);         -- reclaim disk (14 days! see Gotcha #3)
```

---

## 7. Decision log

| # | Decision | Why |
|---|---|---|
| 1 | **Storage = Lance datasets; DuckDB = engine.** One `ATTACH`ed folder per store; one dataset per collection | Full SQL DML works only on attached tables; writes commit straight to Lance (proven — no double-write) |
| 2 | **Connector = `Kurrent.SemanticKernel.Connectors.DuckLance`**, private (not published to NuGet) | Standard `Microsoft.Extensions.VectorData` API on top; keeping it private avoids naming/prefix questions entirely |
| 3 | **Index defaults: quantized (`IVF_PQ`, `IVF_HNSW_PQ` for HNSW)** | Matches our memory/speed priorities; validated live |
| 4 | **`refine_factor` always set for quantized indexes** | Gotcha #1 — without it, small-k results can be plain wrong |
| 5 | **Filters: parameterized SQL `WHERE` + `prefilter := true`**; tag filters via oversample | Gotcha #2 — equality prefilters properly; containment isn't pushed down yet |
| 6 | **Scores follow the declared distance function** (`CosineDistance` raw, `CosineSimilarity` = 1 − distance, `EuclideanSquaredDistance` raw) | Matches the current standard-API convention; Lance's squared-L2 maps natively |
| 7 | **Background reindex scheduler lives in the connector** (query-driven: `SHOW INDEXES` + `COUNT(*)`; append on threshold; time-based retrain; compaction) + public `OptimizeIndexesAsync()` for on-demand | OSS Lance doesn't auto-refresh indexes; correctness never suffers, only latency — the scheduler keeps latency flat |
| 8 | **Vacuum retention = days, never minutes** + recycle-connection on `IO: Not found` | Gotcha #3 — maintenance doesn't refresh per-connection caches |
| 9 | **String keys only (v1)** | Keeps scope tight; int/long trivial to add later, Guid workaround known |
| 10 | **Connection pooling via `Kurrent.Quack`** (existing internal component), used for pooling only | Don't design pooling twice |
| 11 | **Later (track 2): port/upstream to microsoft/semantic-kernel** | The repo's shared conformance test suites + community reach; deltas are catalogued (xUnit, 4 target frameworks, public collection constructors, scheduler extracted to a hosted service) |

---

## 8. Honest list of what's still open

None of these block starting; they're the tail of the validation checklist.

- **Cross-process concurrent writes** — safe by design (same atomic file operations), stress-tested in one process; not yet tested across two processes.
- **Full data-type matrix** — strings, string-lists and vectors validated; bool/int/decimal/timestamp columns still to sweep.
- **Multiple vector columns per record** — proven on the Lance engine directly; not yet re-proven through the DuckDB extension.
- **Hybrid text argument as bound parameter** — the vector binds fine (validated); the text was a literal in our test. Same binding path, expected fine, worth one check.
- **Intel Mac is unsupported** by the extension — needs a conscious "we don't care" (or we keep the old path for that platform).

---

## 9. References

- Test harness (rerunnable): `lancespike/Program.cs` — 56 checks, C# console app
- Full technical spec: `duckdb-connector-spec.md` (same folder) — now updated with every validated fact
- Fact sheet for future sessions: `duckdb-lancedb-reference.md` (same folder)
- [Lance extension — official DuckDB docs](https://duckdb.org/docs/lts/core_extensions/lance) · [extension source](https://github.com/lance-format/lance-duckdb) · [DuckDB blog: Test-Driving Lance](https://duckdb.org/2026/05/21/test-driving-lance) · [LanceDB × DuckDB announcement](https://www.lancedb.com/blog/lance-x-duckdb-sql-retrieval-on-the-multimodal-lakehouse-format)
