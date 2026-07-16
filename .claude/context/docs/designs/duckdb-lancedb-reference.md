# DuckDB + Lance + LanceDB reference

Facts gathered from primary sources (extension source code, official docs, package listings) — current as of mid-2026. Hand this to any assistant/session working on the DuckDB VectorStore connector to skip re-research.

## Packages

- **DuckDB.NET.Data.Full** (NuGet) — ADO.NET provider for DuckDB, includes native library. Current: 1.5.3. .NET Standard/8+. **Not genuinely async, on two independent points:** DuckDB's core engine has no native async I/O at all (still an open, unimplemented feature request in DuckDB's own repo), and DuckDB.NET's own documented API surface exclusively shows synchronous `ExecuteNonQuery`/`ExecuteScalar`/`ExecuteReader` calls. Any `*Async` methods it exposes are, at best, default ADO.NET base-class `Task.Run` wrapping — not true asynchrony.
- **Microsoft.Extensions.VectorData.Abstractions** (NuGet) — current: 10.7.0.
- **LanceDB** (NuGet, from [lennylxx/lancedb-csharp](https://github.com/lennylxx/lancedb-csharp), unofficial) — P/Invoke wrapper around the official Rust `lancedb` crate. Current: 2.4.0. **Platforms: Linux x64, Windows x64, macOS arm64 only** — no macOS x64, no ARM Linux. .NET 8.0+/.NET Standard 2.0.

**DuckDB `lance` extension platform support** (confirmed from DuckDB's own official docs): `linux_amd64`, `linux_arm64`, `osx_arm64`, `windows_amd64` only — no macOS x64 (Intel). Combined with lancedb-csharp's own gap above, Intel Mac has no supported path through either dependency.

## DuckDB `lance` core extension

```sql
INSTALL lance; LOAD lance;   -- or: INSTALL lance FROM community;
```

### Scanning

```sql
SELECT * FROM 'path/to/dataset.lance' LIMIT 10;          -- local
SELECT * FROM 's3://bucket/path/to/dataset.lance' LIMIT 10;  -- object store
```

### Writing (`COPY ... TO ... FORMAT lance`)

```sql
COPY (SELECT ...) TO 'out.lance' (FORMAT lance, mode 'overwrite');  -- create/overwrite
COPY (SELECT ...) TO 'out.lance' (FORMAT lance, mode 'append');     -- append
COPY (SELECT ... LIMIT 0) TO 'empty.lance' (FORMAT lance, mode 'overwrite', write_empty_file true);
```

### Namespaces (attach a directory as a catalog)

```sql
ATTACH 'path/to/dir' AS ns (TYPE LANCE);
CREATE TABLE ns.main.my_table AS SELECT ...;   -- CTAS
SELECT count(*) FROM ns.main.my_table;
DETACH ns;
```

Once attached, full DML/DDL works: `INSERT`, `UPDATE` (with or without `WHERE`), `DELETE`, `TRUNCATE TABLE`, `MERGE INTO` (with `WHEN MATCHED`/`WHEN NOT MATCHED`/`WHEN NOT MATCHED BY SOURCE`, `RETURNING`), `DROP TABLE`, `ALTER TABLE` (add/rename/retype/drop column, comments).

### Search table functions

```sql
-- Vector search: returns _distance (smaller = closer)
SELECT id, _distance FROM lance_vector_search(
  'dataset.lance', 'vec_col', [0.1,0.2,0.3,0.4]::FLOAT[4],
  k := 5, use_index := true, nprobs := 4, refine_factor := 2, prefilter := true
) ORDER BY _distance ASC;

-- Full-text search: returns _score (larger = better)
SELECT id, _score FROM lance_fts(
  'dataset.lance', 'text_col', 'query string', k := 10, prefilter := true
) ORDER BY _score DESC;

-- Hybrid: returns _hybrid_score (larger = better), plus _distance and _score
SELECT id, _hybrid_score FROM lance_hybrid_search(
  'dataset.lance', 'vec_col', [0.1,0.2,0.3,0.4]::FLOAT[4], 'text_col', 'query string',
  k := 10, prefilter := false, alpha := 0.5, oversample_factor := 4,
  nprobs := 4, refine_factor := 2, use_index := true
) ORDER BY _hybrid_score DESC;
```

`alpha`: `0.0` = pure text, `1.0` = pure vector, default `0.5`. `oversample_factor` (default 4): fetch `k * factor` candidates per modality before merging.

**`_distance` value semantics, confirmed empirically via `pylance` with known test vectors** (identical/orthogonal/partial-overlap/opposite): `cosine` and `dot` metrics both compute `1 - x` (`1 - cosine_similarity` and `1 - dot_product` respectively — same framing for both); `l2` computes **squared** Euclidean distance, not raw L2. None of this is documented in the extension's own docs — confirmed only by direct testing.

**Filter semantics for all three:** if `prefilter=false` (default), pushdown is best-effort — on failure DuckDB retries without pushdown and applies filters client-side. If `prefilter=true`, prefilterable filters *must* push down or the query errors. Note the DuckDB extension defaults `prefilter` to `false`; LanceDB's own client defaults pre-filtering to `true` — don't rely on either default, set it explicitly.

**No `ORDER BY` an arbitrary field within these functions.** They rank by relevance (`_distance`/`_score`/`_hybrid_score`) only. To get "hybrid search filtered by X, ordered by some other column, top N," request a larger relevance-ranked candidate pool (`k` >> N) and sort/take client-side — there's no single-call way to do it, and this has an inherent recall tradeoff (a genuinely recent item that narrowly misses the relevance cutoff won't be in the pool).

**Tag/list containment filtering is supported.** Lance's DataFusion filter layer historically lacked `array_contains` ([lance#1115](https://github.com/lance-format/lance/issues/1115), opened Aug 2023) — that issue is **closed**, fixed by PR #1793. Current filtering uses `array_has_any(list_col, [...])` / `array_has_all(list_col, [...])`, both prefilter-pushdown-eligible when a `LABEL_LIST` index exists on the column (per [LanceDB's filtering docs](https://docs.lancedb.com/search/filtering)).

**Vector columns must be fixed-size `ARRAY`** (`FLOAT[dim]`/`DOUBLE[dim]`), not variable-length `LIST`. Cast with `ALTER TABLE ... ALTER COLUMN ... TYPE FLOAT[N]` if needed.

**Multiple vector columns per record work with no surprises** — confirmed empirically (via `pylance`): two independently-dimensioned, independently-indexed (different index types, different metrics), independently-queried vector columns on one dataset, both returning correct nearest-neighbor results.

### Index DDL

```sql
CREATE INDEX idx ON 'dataset.lance' (col) USING <TYPE> WITH (...);
SHOW INDEXES ON 'dataset.lance';
DROP INDEX idx ON 'dataset.lance';
ALTER INDEX idx ON 'dataset.lance' OPTIMIZE WITH (mode = 'append' | 'merge' | 'retrain', num_indices_to_merge = N);
```

Confirmed supported `USING` values (from extension source, `rust/ffi/index.rs`):
- **Vector:** `IVF_FLAT`, `IVF_PQ`, `IVF_SQ`, `IVF_RQ`, `IVF_HNSW_FLAT`, `IVF_HNSW_PQ`, `IVF_HNSW_SQ`
- **Scalar:** `BTREE`, `BITMAP`, `ZONEMAP`, `BLOOMFILTER`/`BLOOM_FILTER`, `INVERTED`, `NGRAM`/`N_GRAM`, `LABELLIST`/`LABEL_LIST`

Vector index params (JSON via `WITH (...)`):
- All: `metric_type` (default `l2`; also `cosine`, `dot`), `num_partitions` (default `256`), `version` (default `v3`)
- `IVF_PQ`: `num_bits` (8), `num_sub_vectors` (16), `max_iterations` (50)
- `IVF_SQ`: `num_bits` (8), `sample_rate` (256)
- `IVF_RQ`: `num_bits` (8 — confirmed from the DuckDB extension's own source, `index.rs`; note LanceDB's own SDK docs give a *different* default, `num_bits: 1`, for the same parameter on the same index type — see the flagged discrepancy below)
- `IVF_HNSW_*`: adds `hnsw_m`, `hnsw_ef_construction`, `hnsw_max_level`, `hnsw_prefetch_distance`, plus PQ/SQ params for quantized variants

**Constraint:** single-column indices only — no composite/multi-column index support. `IVF_RQ` additionally requires the vector dimension itself be divisible by 8 (RaBitQ packs 1 bit/dimension) — noted for completeness, but out of scope for the connector (quantized `IVF_PQ`/`IVF_HNSW_PQ` and unquantized `IVF_FLAT`/`Flat` are the actual priority; `IVF_RQ` was never reachable through the abstraction's `IndexKind` mapping anyway).

**`num_bits` discrepancy, not being chased further:** `IVF_RQ`'s default is `8` per the DuckDB extension's own source, but `1` per LanceDB's SDK docs — moot given the above.

**`IVF_PQ`'s `num_sub_vectors` must evenly divide the vector dimension** — confirmed empirically (via `pylance`), Lance itself gives a clear error rather than a cryptic panic: `"dimension (10) must be divisible by num_sub_vectors (4)"`.

### Concurrency & transactions

Lance has genuine MVCC with ACID guarantees ([transaction model docs](https://lance.org/format/table/transaction/)): every commit is an atomic storage operation (`rename-if-not-exists`/`put-if-not-exists`). Concurrent-write conflicts resolve in three tiers:
- **Rebasable** — e.g. two concurrent deletes on different rows — merged automatically, transparent.
- **Retryable** — e.g. an update racing a concurrent compaction — retried automatically against the new version.
- **Incompatible** — genuinely irreconcilable (e.g. delete racing a restore) — hard failure.

Retry exhaustion under pathological contention is a real, documented failure mode: `"Failed to commit the transaction after 5 retries"` ([lance#1836](https://github.com/lance-format/lance/issues/1836)).

**Empirically verified** (via `pylance`): 8 threads × 15 concurrent `merge_insert` upserts, all targeting the *same row* (maximum contention) — 120/120 succeeded, zero exceptions, one deterministic winner (last commit wins), no lost updates. ~62 commits/sec under max contention vs. ~230/sec for disjoint concurrent appends (separate test) — slower, not less correct.

**Confirmed at the DuckDB extension's own source level, not just the underlying engine:** `rust/ffi/write.rs` (plain insert) uses `lance::dataset::{CommitBuilder, InsertBuilder, WriteParams}`; `rust/ffi/merge.rs` (the `MERGE INTO`/upsert path) commits via `lance::dataset::transaction::{Operation, Transaction}` — the same primitives behind the OCC model above, not a separate implementation.

### Index staleness & reindexing (confirmed)

Lance indices do **not** auto-update on writes at the OSS level (only LanceDB Cloud/Enterprise auto-reindexes in the background). But correctness is preserved regardless: an index segment doesn't need to cover every fragment — queries automatically split into an indexed subplan + a brute-force scan of unindexed fragments, then merge. Confirmed explicitly for FTS ("Lance still returns those rows in `full_text_query` results... scans unindexed fragments with flat search, and then merges the results" — [Lance FTS docs](https://lance.org/quickstart/full-text-search/)); the same `num_unindexed_rows`/`index_stats()` mechanism applies generally. Net effect: new writes are never silently missing from results, but latency degrades as unindexed rows accumulate until reindexed.

Reindex via `ALTER INDEX ... OPTIMIZE WITH (mode := 'append' | 'merge' | 'retrain')` (`append` = fold in new fragments, `merge` = consolidate segments, `num_indices_to_merge` supported, `retrain` = full rebuild).

Indexes can be created empty and populated later: `train=false` at creation time registers a deferred/empty index instantly — confirmed in the DuckDB extension's own FFI signature, `lance_dataset_create_index(dataset, index_name, columns, columns_len, index_type, params_json, replace, train)`.

**Naming rule** (confirmed from LanceDB's namespace docs): Lance namespace/table names must be non-empty, letters/numbers/underscores/hyphens/periods only.

### Maintenance

```sql
OPTIMIZE 'dataset.lance' WITH (target_rows_per_fragment = 1048576, materialize_deletions = true, ...);
VACUUM LANCE 'dataset.lance' WITH (older_than_seconds = 1209600, retain_n_versions = 3);
ALTER TABLE 'dataset.lance' SET AUTO_CLEANUP WITH (interval = 1, older_than = '1h', retain_versions = 3);
SHOW MAINTENANCE ON 'dataset.lance';
```

**Disk usage can temporarily *increase* during `OPTIMIZE`'s compaction step** — new compacted files are written before old-version files are deleted. Space is only reclaimed once old versions are pruned via `VACUUM LANCE`'s retention window. Confirmed compaction is a genuinely separate operation from index freshness — calling only `compact_files()` against a dataset with known-unindexed rows left `num_unindexed_rows` completely unchanged.

## lancedb-csharp (`LanceDB` NuGet) — quick API map

**Layering, confirmed from source:** the DuckDB extension is built directly on the lower-level `lance` Rust crate (`lance::dataset::{CommitBuilder, InsertBuilder, ...}`, `lance_index::optimize::OptimizeOptions`, etc.) — not the higher-level `lancedb` crate that lancedb-csharp wraps. This explains several capability gaps between the two paths: `fast_search` (skip brute-force scan of unindexed rows, trading recall for latency) is a documented `QueryBase` method in the `lancedb` crate (`"Only execute the query over indexed data"`, default `false`) but **confirmed absent** from the DuckDB extension's search source (`rust/ffi/search.rs` — only `prefilter`/`use_index` exist, checked directly). Similarly, lancedb-csharp's rerankers (`RRFReranker`/`LinearCombinationReranker`/`MRRReranker`) have no DuckDB-extension equivalent — DuckDB's hybrid search only does `alpha`-weighted blending.

```csharp
var connection = new Connection();
await connection.Connect("/path/to/db");
var table = await connection.CreateTable("docs", new CreateTableOptions { Schema = schema });

// Index creation — every type below is available directly as a typed class
await table.CreateIndex(["vector"], new IvfPqIndex { DistanceType = DistanceType.Cosine });
await table.CreateIndex(["vector"], new HnswSqIndex { DistanceType = DistanceType.Cosine, NumPartitions = 4 });
await table.CreateIndex(["tags"], new LabelListIndex());
await table.CreateIndex(["text"], new FtsIndex { WithPosition = true, Language = "English" });

// Upsert via merge
await table.MergeInsert("id").WhenMatchedUpdateAll().WhenNotMatchedInsertAll().Execute(newData);

// Hybrid search with real rerankers (RRF, Linear Combination, MRR) — richer than DuckDB's alpha blend
var results = await table.Query()
    .NearestTo(queryVector)
    .NearestToText("search terms")
    .Rerank(new RRFReranker())
    .Limit(10)
    .ToArrow();
```

Full index type list: `BTreeIndex`, `BitmapIndex`, `LabelListIndex`, `FtsIndex`, `IvfFlatIndex`, `IvfPqIndex`, `IvfSqIndex`, `IvfRqIndex`, `HnswPqIndex`, `HnswSqIndex`.

### DuckDB extension FFI source map (`rust/ffi/`)

For anyone continuing source-diving: `arrow_export`, `dataset` (open/close/count/schema/fragments), `dir_namespace` (directory namespace ops), `exec`, `index` (`CREATE INDEX`, `ALTER INDEX ... OPTIMIZE`), `knn`, `merge` (`MERGE INTO`), `namespace` (REST namespace ops), `projection`, `query_table`, `scan`, `schema_evolution` (`ALTER TABLE`), `search` (`lance_vector_search`/`lance_fts`/`lance_hybrid_search`), `session`, `stream`, `take`, `types`, `update`, `util`, `write` (`INSERT`). No prebuilt binaries are published as GitHub release assets — only source tarballs.

## Key source links

- DuckDB lance extension SQL reference: https://github.com/lance-format/lance-duckdb/blob/main/docs/sql.md
- Index creation source (ground truth for supported `USING` types/params): https://github.com/lance-format/lance-duckdb/blob/main/rust/ffi/index.rs
- Filter function confirmation (`array_has_any`/`array_has_all`, `LABEL_LIST`): https://docs.lancedb.com/search/filtering
- lancedb-csharp: https://github.com/lennylxx/lancedb-csharp
- Namespaces and the Catalog Model — LanceDB docs: https://docs.lancedb.com/namespaces
- Quantization — LanceDB docs: https://docs.lancedb.com/indexing/quantization
- Lance Extension — official DuckDB docs: https://duckdb.org/docs/lts/core_extensions/lance.html
- DuckDB `vss` extension (native HNSW, not used in this architecture but relevant if reconsidering): https://duckdb.org/docs/current/core_extensions/vss
- DuckDB `fts` extension (native BM25, not used in this architecture): https://duckdb.org/docs/current/core_extensions/full_text_search
