---
title: DuckDB + Lance Knowledge Base
status: accepted
date: 2026-07-19
tags: [duckdb, lance, ducklance, vector-search, kontext]
related: [docs/designs/duckdb-lancedb-reference.md, docs/designs/ducklance-validation-report.md, docs/designs/duckdb-connector-spec.md]
---

# DuckDB + Lance Knowledge Base

Living reference for everything this codebase knows about the DuckDB `lance` extension and the Lance
format. Consolidates the live-validated findings from the spike/validation work (see `related:` docs —
they remain the raw evidence; this doc is the organized index) plus knowledge accumulated during the
DuckLance/KontextStore build-out. **Trust order when anything conflicts: live behavior of the extension
> the validation report / spike harness > this document > upstream docs.**

Provenance markers: **[validated]** = confirmed against the real extension (DuckDB v1.5.3, lance build
`533e0ee`, osx_arm64, 2026-07-17/18). **[source]** = confirmed by reading extension source. Unmarked
claims are standard behavior of the underlying tech (labeled where inferred).

## 1. What this stack is

- **Lance** is a columnar data format for vector workloads. A *dataset* is an on-disk directory
  (`<name>.lance/`) holding data fragments, versioned manifests, and indexes. "LanceDB" the database
  product is a different, higher layer.
- **The DuckDB `lance` extension** embeds the low-level `lance` Rust crate (NOT the `lancedb` crate)
  **[source]**. This layering explains its capability gaps vs LanceDB SDKs: no `fast_search`, no
  rerankers (only `alpha` blending), no `filter :=` parameter on hybrid search.
- **DuckDB stores nothing** — all DML commits go directly to the Lance dataset; the `.ddb` database
  file holds only catalog state **[validated + source]**.
- **Platforms**: linux x64/arm64, macOS arm64, windows x64. **No macOS x64 (Intel)** — no supported
  path at all on Intel Macs.
- DuckDB has **no true async API** — .NET `*Async` surfaces are sync-under-async. Our managers run
  work inline on the caller's thread (measured: query cost ~0.5 ms dominates; a `Task.Run` hop added
  only scheduling noise and allocations).

## 2. Storage model and addressing

- **Dual addressing** — the single most confusing thing in this stack:
  - *Qualified table name* (`<alias>.main.<table>`) — for DML and the search table functions.
  - *Raw dataset path* (`/dir/<table>.lance`) — required by index DDL (`CREATE INDEX`,
    `ALTER INDEX ... OPTIMIZE`, `VACUUM LANCE`), which does not accept the qualified name.
- **ATTACH is instance-scoped, not connection-scoped** **[validated]**. DuckDB.NET shares one native
  instance per database-file path; once any connection attaches an alias, a bare re-`ATTACH` on
  another connection fails ("database with name X already exists"). Always `ATTACH IF NOT EXISTS`.
- **`ATTACH IF NOT EXISTS` is check-then-create, not atomic** **[validated in production code]**: two
  connections racing initialization can still make the loser throw "already exists" even though the
  attachment landed. Within one store the racing attach is always same-path, so the error is benign —
  our pool `Initialize` catches exactly that message and treats it as success.
- **The database file's stem must not equal the attach alias** **[validated — silent data loss]**.
  DuckDB names the database's own catalog after the file stem (up to the first `.`). If stem == alias,
  `ATTACH IF NOT EXISTS` silently no-ops against the database's own catalog and writes route away from
  Lance. Our constructors reject such paths outright.
- **Single-process ownership**: the database file is opened READ_WRITE with an exclusive OS lock; a
  second process fails fast ("Could not set lock on file"). The Lance dataset directory itself stays
  multi-reader via read-only connections or other Lance tooling.
- **Engine introspection [validated 2026-07-20, v1.5.3]** — get the info first, then create what
  needs creating: `SELECT version(), current_database(), current_schema()`; `duckdb_databases()` →
  `database_name, path (NULL for internal rows), type, readonly, internal` (+ encrypted/cipher/options);
  `duckdb_extensions()` → `extension_name, loaded, installed, extension_version, install_mode`;
  `duckdb_settings()` → `name, value, scope` — values are the engine's VARCHAR renderings
  (`memory_limit` → `"512.0 MiB"`), names mixed-case (`TimeZone` vs `access_mode`). Wrapped as
  `DuckDBEngineInfo.From(connection | connectionString)` in DuckLance. Also validated:
  Kurrent.Quack's `DuckDBAdvancedConnection` extends `DuckDB.NET.Data.DuckDBConnection`, so
  `DuckDBConnection`-typed APIs accept pooled Quack connections directly.
  **An ATTACHed TYPE LANCE database reports `type='duckdb'` in `duckdb_databases()`
  [validated 2026-07-20 — the type column CANNOT prove a lance attach].** The deterministic
  stem-collision detector is name identity instead: on the silent no-op the alias resolves to the
  engine's OWN catalog, so `alias == current_database()` ⇒ collision; a healthy attach is a
  separate catalog entry (`KontextConnectionPool.VerifyLanceNamespace` is the reference).
- **Recursive CTEs work over lance scans [validated 2026-07-20 by the lineage tests]**:
  `WITH RECURSIVE family AS (... UNION ...)` joining a lance table against the CTE, plus an
  `IN (SELECT …)` over the result, behaves exactly like plain DuckDB — including the set-based
  `UNION` recursion semantics (already-produced rows are not re-expanded; recursion terminates
  when no new row appears). Reference: `KontextDataStoreV2.GetLineageAsync`.
- **Our convention (DuckLance & KontextStore)**: caller supplies the full database file path; its
  directory is the Lance namespace; one `<collection>.lance` dataset per collection; table name IS the
  collection name; attach alias `ldb` (DuckLance) / `kx` (KontextStore).

## 3. Naming and identifiers

- **Lowercase column names only** **[validated — silent no-op]**: Lance's `DELETE`/`UPDATE` predicate
  pushdown silently does nothing on mixed-case identifiers. A retract that "succeeds" but changes no
  rows. All our columns are lowercase.
- Our table-name rule is `^[A-Za-z0-9_]+$` — deliberately stricter than Lance's own (which also allows
  `-` and `.`): the name is used as an unquoted SQL identifier and inside search-function URI strings,
  where `.` is the catalog separator and `-` parses as an operator.

## 4. Writing and concurrency

- **Every Lance write is an atomic commit** (MVCC, put-if-not-exists). Conflicts resolve in three
  tiers: rebasable (auto-merged), retryable ("Retryable commit conflict ... Please retry."), and
  incompatible (hard failure). Retry exhaustion is a real documented failure
  ("Failed to commit the transaction after 5 retries", lance#1836).
- **Batch upserts = one multi-row `MERGE` = one commit** — dramatically shrinks the conflict window vs
  N per-row commits and avoids an O(n²) serial-loop cost **[validated]**. Preconditions: de-duplicate
  keys in-batch (last wins) because `MERGE` evaluates source rows against the PRE-merge target — two
  same-key rows absent from the target both insert; and chunk large batches (we cap at 500 rows).
- **Retry discipline** (both managers): "Retryable commit conflict" → retry same connection, ×3 with
  20/50 ms backoff. Contention measured upstream: ~62 commits/s same-row max contention, ~230/s
  disjoint — slower, never incorrect.
- **Multi-tag containment binds one list parameter**: `array_has_any(tags, CAST(? AS VARCHAR[]))` with
  the whole list bound as a single parameter. Binding values inside an array literal `[?]` fails
  **[validated — genuine bug found by tests]**.

## 5. Indexing

- **Available types [source: `rust/ffi/index.rs`]** — vector: `IVF_FLAT`, `IVF_PQ`, `IVF_SQ`,
  `IVF_RQ`, `IVF_HNSW_FLAT`, `IVF_HNSW_PQ`, `IVF_HNSW_SQ`; scalar: `BTREE`, `BITMAP`, `ZONEMAP`,
  `BLOOMFILTER`, `INVERTED` (BM25 full-text), `NGRAM`, `LABEL_LIST` (tag containment). Single-column
  only.
- **The 256-row training floor** **[validated — exactly 256, engine-declared]**: creating a vector
  index below the floor fails with one of TWO error shapes [both observed live 2026-07-20]:
  empty table → "Creating empty vector indices with train=False is not yet implemented";
  non-empty but under 256 → "Unprocessable: Not enough rows to train PQ. Requires 256 rows but
  only N available". The FFI *signature* has a `train` parameter, but the untrained path is
  unimplemented in this extension version. Preferred detection: try the CREATE and match BOTH
  messages (`KontextSchema.IsBelowTrainingFloor` is the reference) — a client-side
  `count(*) >= 256` copies an engine-internal rule that can drift. Consequences:
  - Index creation must be **lazy**. Our pattern is stateless polling — no client-side row tracking:
    `EnsureVectorIndexes` asks the database `SHOW INDEXES` + `SELECT count(*)` live and creates the
    index when count ≥ 256. It is invoked automatically by (a) the always-on reindex scheduler every
    tick (default 5 min), (b) re-opening an existing collection, (c) manual optimize. Self-healing
    across crashes because nothing is remembered.
  - Below the floor, searches are brute-force scans: exact and trivially fast at that scale.
  - **Below the floor, "no index" is the OPTIMUM — no index type can improve results there.**
    Brute force IS exact nearest-neighbor; every index (PQ or not) only trades accuracy for
    latency, and at ≤256 rows there is no accuracy above exact and no latency worth buying.
    Don't chase indexes for small tables.
  - **The floor is PQ-specific** (the error names PQ codebook training — 256 samples). FLAT-family
    indexes (`IVF_FLAT`, `IVF_HNSW_FLAT`) have no codebook and may be creatable below 256
    [inferred from the error's wording, not probed] — pointless per the previous bullet, so
    deliberately not pursued.
  - **Above the floor, PQ already matches FLAT on results**: `refine_factor := 4` re-ranks the
    top-k with exact distances [validated — identical distances pre/post index], so
    `IVF_HNSW_PQ` gives FLAT-parity results at a fraction of the memory. FLAT variants would
    only matter if refine ever became the bottleneck.
- **Scalar/FTS indexes have no such floor** — they work at 0 rows and are created eagerly.
- **Scalar index creation validated [2026-07-20, KontextSchema tests]**: `CREATE INDEX … USING
  BTREE` (memory_id, superseded_by) and `USING LABEL_LIST` (tags) both succeed on an EMPTY lance
  table — no training floor, unlike vector indexes. LABEL_LIST caveat: containment predicates
  never push down (§6), so a tags LABEL_LIST index is DORMANT today — created for
  forward-compatibility only; the BTREE indexes can only matter for pushed-down equality
  [index *use* by the scan is inferred from Lance semantics, not measured here].
- **Index staleness is a latency problem, never a correctness problem**: queries automatically split
  into an indexed subplan + a brute-force scan of unindexed fragments and merge results — new writes
  are **never missing** **[validated + Lance docs]**. The reindex scheduler exists to bound the
  unindexed tail's latency cost, not to make results correct.
- **Maintenance** — all against the raw dataset path; inside `WITH (...)` always `=`, never `:=`
  **[validated]**:
  - `ALTER INDEX ... OPTIMIZE WITH (mode = 'append')` folds new rows in; `mode = 'retrain'` rebuilds.
  - `OPTIMIZE '<path>'` compacts fragments/tombstones — a genuinely separate operation from index
    freshness (compaction leaves `num_unindexed_rows` unchanged) **[validated]**. Disk usage can
    temporarily grow during compaction until old versions are vacuumed.
  - `SHOW INDEXES` returns `index_name, index_type, fields, rows_indexed, details` — **no timestamp**,
    so a retrain cadence needs its own stored clock (our scheduler keeps one in memory). Index types
    come back squashed-PascalCase (`IvfHnswPq`); we classify vector indexes by normalized `IVF` prefix.
  - Kontext-side owners: `KontextSchema` issues every maintenance statement (`RetrainVectorIndexAsync`,
    `CompactAsync`, `VacuumAsync`, plus the `ExistsAsync` / `GetMaintenanceStateAsync` probes);
    `KontextMaintenanceScheduler` owns the tick cadence and the pure per-tick `Decide`
    (ensure-vs-retrain-vs-nothing), calling `EnsureVectorIndexAsync` for both catch-up creation and
    backlog folds. Ticks are serialized (non-overlap gate), so vacuum never runs concurrently.
- **PQ constraint**: `num_sub_vectors` must evenly divide the vector dimension (clear engine error).

## 6. Search and scoring

Three table functions **[validated]**: `lance_vector_search` (returns `_distance`, smaller = closer),
`lance_fts` (`_score`, larger = better), `lance_hybrid_search` (`_hybrid_score`, larger = better, plus
`_distance` and `_score` as diagnostics).

- **Full named-parameter surfaces [validated 2026-07-20 via `duckdb_functions()`]**:
  `lance_hybrid_search` → `k, alpha, prefilter, refine_factor, oversample_factor, nprobs, use_index`
  (note the spelling `nprobs`; `use_index := false` forces an exact brute-force scan even when an
  index exists — the exactness escape hatch). `lance_vector_search` → the same minus alpha/oversample
  plus `explain_verbose`. `lance_fts` → only `prefilter, k`. Query-vector overloads exist for both
  `FLOAT[]` and `DOUBLE[]`. Confirmed absent: `filter`, `fast_search`, reranker knobs.
- **All named arguments accept bound `$name` parameters [validated 2026-07-20 via live probe]**:
  `k := $k`, `prefilter := $prefilter`, `refine_factor := $refine_factor`, `alpha := $alpha`,
  `oversample_factor := $oversample_factor` all bind through DuckDB.NET named `DuckDBParameter`s,
  as do the query vector (`CAST($query_embedding AS FLOAT[N])`), the query text, and `LIMIT $limit`.
  The same `$name` referenced twice in one statement binds once. An earlier claim that Lance named
  arguments "cannot be bound as parameters" was wrong — do not interpolate knob values into SQL
  text. What genuinely cannot be a parameter: the `FLOAT[N]` type dimension and SQL keywords
  (ASC/DESC) — those remain interpolated/clause-picked.
- **Return columns [validated 2026-07-20]**: each function returns ALL table columns (including the
  vector column — project explicitly to avoid paying for it) plus its scores: vector → `_distance`;
  fts → `_score`; hybrid → `_distance`, `_score`, AND `_hybrid_score`. All single-precision FLOAT.
  **A row surfaced by only one leg has NULL in the other leg's diagnostic column** — only the blend
  is always present (surfaced by failing tests, then pinned). The blend behaves as per-leg
  min-max-normalized scores combined by alpha [inferred from one probe — leg ties flatten to
  identical `_hybrid_score` values].

- **`_distance` semantics — undocumented upstream, measured with known vectors [validated]**:
  - `l2` (default) returns **squared** Euclidean distance, not raw L2.
  - `cosine` and `dot` return `1 − similarity`.
  - Identical values pre-index (brute force) and post-index (PQ-family with `refine_factor := 4`):
    probes returned `0 / 2 / 4` (squared L2) and exact `0 / 1 / 2` (cosine) in **both** states.
  - For L2-normalized vectors, squared-L2 = `2 − 2·cos`, so `similarity = 1 − d/2` is one correct
    conversion for every state (KontextStore's formula; getting this wrong pre-index was a real bug the
    tests caught).
- **`refine_factor := 4` is mandatory with PQ-family indexes** **[validated]**: without it,
  quantization error scrambles small-k ranking (a k=1 search for an identical vector returned the
  wrong row). With it, top-k candidates are re-ranked with exact distances — returned scores are true
  distances.
- **Prefilter semantics** **[validated]**: `WHERE` around the table function is the filter mechanism
  (the `filter :=` named parameter is not in the released build; at HEAD it is ENFORCED
  REST-namespace-only — `lance_search.cpp` throws "filter parameter is only supported for
  namespace-backed tables" for local datasets [source-confirmed 2026-07-20], so it is not a future
  local escape hatch either). `prefilter := true` = genuine
  prefilter (k matching rows even if far outside the global top-k); `prefilter := false` = post-filter
  (fewer than k). Always set it explicitly. Parameter-bound predicates (`WHERE category = ?`) push
  down fine.
- **Containment (`array_has_any`) is NOT pushed down** **[validated + source: no translation in
  `lance_filter_ir.cpp`]** — silently post-filterish even with `prefilter := true` and a `LABEL_LIST`
  index. Correct fallback: **oversample** — set `k` to cover the whole candidate set (we use the table
  row count), let DuckDB evaluate the containment predicate, then `LIMIT n`. Equality predicates DO
  push down and need no oversampling.
- **The full pushdown whitelist [source-confirmed 2026-07-20 at HEAD of `lance-format/lance-duckdb`,
  clone: `~/dev/contrib/lance-duckdb`]**: column refs (incl. `struct_extract` nested), constants,
  non-try constant casts, all comparisons, AND/OR conjunctions, NOT, IS [NOT] NULL, IN/NOT IN
  (literal lists), BETWEEN, and exactly five scalar functions — `lower`, `upper`, `starts_with`,
  `ends_with`, `contains` (STRING contains, VARCHAR needle) — plus LIKE/ILIKE and
  `regexp_matches`/`regexp_full_match`. Array containment is absent from the wire IR itself (both
  the C++ encoder `lance_filter_ir.cpp` AND the Rust decoder `rust/expr_ir.rs` lack any opcode) —
  adding it upstream means changing both sides in sync (per the repo's AGENTS.md). Conjunction-AND
  of equalities is explicitly handled (`BOUND_CONJUNCTION`/`CONJUNCTION_AND`) — multi-column scope
  filters translate. HEAD ⊇ released build: absences at HEAD are certainly absent in our build;
  presences should still be live-probed before relying on them.
- **Pushdown OBSERVED via EXPLAIN [validated 2026-07-20, CLI engine v1.5.4 / lance 3500606 —
  translator identical to our 533e0ee for these paths (no filter_ir commits between the builds)]**:
  the plan itself proves pushdown — look for `Lance Pushed Filter Parts: N` and the predicate
  inside `LanceRead: … full_filter=…` (pushed) vs a separate `FILTER` operator above the scan with
  `Pushed Filter Parts: 0` (not pushed). Observed: `tenant_id = 'a' AND user_id = 'b'` → 2 parts
  pushed, whole conjunction in full_filter; `array_has_all(tags, […])` → 0 parts, FILTER above
  scan; `contains(tags_text, '|tag|')` → 1 part pushed. This is the definitive probe technique for
  any future pushdown question.
- **No arbitrary `ORDER BY` inside the functions** — they rank by relevance only. "Filtered, ordered
  by another column, top N" requires a large k pool + client-side sort, with an inherent recall
  tradeoff.
- **Hybrid**: `alpha := 0.5` blends the legs equally (0 = pure text, 1 = pure vector);
  `oversample_factor` (default 4) fetches k×factor per modality before merging. No reranker support
  (that's the `lancedb` crate layer). `lance_hybrid_search` has **no filter parameter even on main** —
  splice a plain SQL `WHERE` (validated to work alongside `prefilter := true`).
- **`fast_search` does not exist in the extension** **[source: `rust/ffi/search.rs`]** — the
  lancedb-crate flag that skips the unindexed tail (recall-for-latency trade) is absent, which is
  what guarantees the tail is always scanned. See §9 upgrade tripwires.

### 6.1 Recall-score stability (the "256 cliff" question)

There is **no score cliff when the vector index appears at 256 rows**:

- The distance function returns identical values pre- and post-index (measured, §6 above), and the
  similarity conversion is one formula for both states. A memory scoring 0.87 at 200 rows scores 0.87
  at 300.
- What CAN differ post-index is *membership at the margins*: ANN search may occasionally surface a
  different near-tie at the bottom of the top-k than an exhaustive scan would. With
  `num_partitions = 1` + HNSW + `refine_factor := 4`, divergence is rare and confined to near-ties.
- The real score drift lives elsewhere and has nothing to do with indexing: the **BM25 keyword leg**
  of hybrid recall is corpus-relative by construction (IDF shifts as documents accumulate — standard
  IR behavior, not spike-verified in this stack). Absolute `_hybrid_score` values drift continuously
  from row 1 onward. Practical rule: treat `MinScore` as a coarse relevance gate, never a calibrated
  threshold, and never persist scores expecting cross-time comparability.

## 7. Types and values

- **Vector columns must be fixed-size `ARRAY`** (`FLOAT[N]`), never variable `LIST`. Bind query
  vectors as `CAST(? AS FLOAT[N])`. Multiple independently-indexed vector columns per record work
  **[validated]**.
- **Bind shapes are flexible; read shapes are not** **[validated 2026-07-19]**: `DuckDBParameter`
  accepts BOTH `T[]` and `List<T>` for LIST/ARRAY binds (`string[]`, `float[]` probed — identical
  results to the List forms). Reads always come back as `List<T>` from `GetValue`/`GetFieldValue` —
  the driver's choice, convert at the read boundary if your record wants arrays.
- **Pin the session time zone**: `SET TimeZone='UTC'` at connection init, bind `.UtcDateTime`, read
  with `DateTimeKind.Utc` — otherwise `TIMESTAMPTZ` round-trips shifted by the machine's local offset
  **[validated — real bug found by exact-timestamp assertions]**.
- **SQL literal encoding facts (v1.5.x, for the no-parameter-channel quack path)** **[validated]**:
  a raw NUL inside a quoted literal is a parser terminator with no escape (reject such strings);
  `byte[]` → fully hex-escaped `CAST('\xNN...' AS BLOB)`; non-finite floats are quoted words
  (`'NaN'`, `'Infinity'`) cast to type; `FLOAT[0]` is illegal so an empty float collection becomes a
  variable-length `CAST([] AS FLOAT[])` LIST.

## 8. Failure modes and recovery

| Failure | Shape | Recovery **[validated]** |
|---|---|---|
| Transient commit conflict | "Retryable commit conflict for version N ... Please retry." | Retry same connection, bounded (×3, short backoff) |
| Stale dataset handle | `LanceError(IO) ... Not found` or "belongs to non-existent fragment" | Never converges on the same connection — dispose it (don't return it to the pool) and retry once on a fresh connection (re-ATTACH opens a current view) |
| ATTACH race at pool init | `database with name "<alias>" already exists` despite IF NOT EXISTS | Benign when same-path (always true within one store) — treat as success |
| Vacuum-poisoned cache | Aggressive `VACUUM LANCE` deletes files a live cached handle references | Prevention: retention comfortably longer than any handle lifetime. Maintenance ops invalidate NOTHING in the extension's dataset cache **[source: `lance_maintenance.cpp`]** — only index DDL or a fresh connection recovers in-session |
| Native panic under concurrent vacuum | Observed once in the cleanup path | Keep vacuum manual/rare and never concurrent until understood upstream |

## 9. Upgrade tripwires

Re-verify these on ANY extension/DuckDB upgrade — each guards a live-validated behavior:

1. **`fast_search` appearing** in the extension's search functions: enabling it would silently convert
   the unindexed-tail guarantee (correctness) into a recall-for-latency trade.
2. **`train=false` becoming implemented**: would allow eager index creation and remove the 256-row
   lazy floor (simplification opportunity, not a risk).
3. **`filter :=` becoming usable for local datasets**: containment filters would become
   pushdown-eligible (goes verbatim to Lance's filter layer) — the oversampling workaround could go.
4. **Mixed-case pushdown fix**: would relax the lowercase-columns rule (keep it anyway; it costs nothing).
5. **`_distance` semantics**: re-run the known-vector probes (0/2/4, 0/1/2) before trusting scores.
6. Upstream bugs worth reporting/tracking: mixed-case DELETE/UPDATE pushdown no-op; "non-existent
   fragment" stale handles; the concurrent-vacuum native panic; the ATTACH IF NOT EXISTS race;
   Kurrent.Quack's `Open()` connection leak when `Initialize` throws; DuckDB.NET's
   `DuckDBDataReader.GetOrdinal` breaking the ADO.NET contract by throwing `DuckDBException`
   ("Column with name X was not found") instead of `IndexOutOfRangeException` for a missing column
   **[validated 2026-07-19 — surfaced by the mapper's ordinal caching]**.

## 10. Design guidance: what belongs in Lance (Kontext)

**Lance is a search index, not a database** — store the query surface, keep the truth elsewhere.
With hybrid-always as a fixed constraint (Kontext recall is never vector-only), the floor is
`content` (INVERTED) + `vec` (FLOAT[N]). Beyond that, a field earns a column only as:

- **Filter surface** (executes inside the search SQL): tags, type, lifecycle flags, cited ids —
  indexed per their access pattern (`LABEL_LIST` for containment, `BTREE` for equality).
- **Sort keys** (recollect ordering): importance, retained-at, last-accessed-at.
- **Lean projection**: whatever lean recall returns, so the hot path is one hop.

**Round-trip-only payloads (e.g. Kontext's Evidence protobuf blob) do NOT belong in a
stamped-lifecycle row.** Lance fragments are immutable: every UPDATE rewrites the whole row into a
new fragment — so retract/supersede stamps and access-touch refreshes re-pay the blob's bytes every
time, in write amplification, compaction, and version retention. Flatten the query-shaped projection
into indexed columns (the `CitedMemoryIds` pattern) and keep the blob with the source of truth.
Under the stream-first design, a `log_position` column replaces the body entirely: full reads fetch
the event at that position. Standalone-store mode keeps the blob in Lance as a known concession.

Capability answers that recur (validated where marked, otherwise columnar-mechanics reasoning):
indexes are derived structures in `_indices/` — DROP/CREATE and OPTIMIZE retrain **[validated]**
never rewrite table data, so changing index type/params is always cheap; column WIDTH is free to
read (queries touch only referenced columns) but taxed to mutate (UPDATE rewrites the whole row);
ROW growth is gentle on indexed paths (IVF/HNSW sublinear, BM25 posting lists) with only the
unindexed tail linear; a single all-in-one lance table is fully viable (today's KontextStore) and
rebuildable via CTAS **[validated]** — the duck/lance split buys stamp economics and listing
ergonomics, not capability; split-storage overhead is ~1.1× without a duck-side vector copy,
~1.8× with one (the copy buys no-re-embedding rebuilds; without it, rebuild = re-embed, which
doubles as the model-migration path).

Operational digest (each validated above): lowercase columns; UTC sessions; canonical tag encoding,
normalized at write (containment is exact-match only); oversample containment filters; `refine_factor := 4`
with PQ; L2-normalize + `1 − d/2`; batch upserts into one MERGE; buffer access-touches (each is a row
rewrite); conservative, never-concurrent vacuum; one writer process per database file; never persist
scores; add an embedding-model-id column the moment more than one model can ever have written vectors.

## 11. Where the evidence lives

- `docs/designs/duckdb-lancedb-reference.md` — the raw fact sheet with upstream links and the
  [validated]/[corrected] audit trail.
- `docs/designs/ducklance-validation-report.md` — the 56-check spike results; `lancespike/` is the
  rerunnable harness whose outputs are the golden oracles.
- `docs/designs/duckdb-connector-spec.md` — the connector design spec.
- `src/Kurrent.Kontext.DuckLance/` — the MEVD connector; body comments carry the per-site rationale.
  Record ↔ storage mapping is the `RecordCodec` layer (`Mapping/`): named vector slots
  (`VectorText`/`VectorSlots`), positional Encode/Decode per the codec law, `SingleVectorRecordCodec`
  as the hand-written tier. Resolution: a codec registered on `DuckDBVectorStoreOptions.Codecs` wins;
  `DuckDBModelCodec` is the unconditional model-driven default. The old `DuckDBRecordMapper` is gone.
- `src/Kurrent.Kontext/VectorStore/` — KontextStore, the abstraction-free distillation, and its README.
