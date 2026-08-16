---
title: Kontext SQLite Search Backend Prototype — Tech Spec
status: draft            # draft | review | accepted | superseded
authors: [sergio]
date: 2026-08-13
tags: [kontext, sqlite, sqlite-vec, fts5, vector-search, hybrid-search, prototype]
---

# Tech Spec — Kontext SQLite Search Backend Prototype

<!--
The HOW — distilled from the design space, implementation-grade. A qualified reader should be able to
build this cold. Current-state document: keep it in sync with the code as it lands. Sources this spec
cites go in spec/refs/.
-->

## Overview

This specification defines a side-by-side SQLite backend prototype for Kontext memories and records.
The prototype uses SQLite FTS5 for BM25, `sqlite-vec` for KNN, ordinary SQLite indexes for scalar
lookups, and SQL or existing C# stages for hybrid fusion. It must preserve the observable retrieval
and projection behavior of the current DuckDB plus Lance backend before it explores approximate
indexes.

The first implementation uses exact float KNN. It is the functional baseline and recall oracle. A
second experiment may evaluate `sqlite-vec` rescore and DiskANN. The prototype does not use the
experimental SQLite IVF implementation and does not claim to reproduce Lance `IVF_HNSW_PQ`
exactly.

### Decision requested

Approve a bounded prototype and benchmark, not a production migration. The prototype will answer
whether SQLite provides enough functional parity, search quality, throughput, and operational
simplicity to remain a supported Kontext backend option.

### Scope

In scope:

- Kontext `memories` and `records` derived search stores.
- BM25, exact KNN, hybrid search, scalar filters, tags, lineage reads, and checkpoints.
- .NET 10 integration through `Microsoft.Data.Sqlite`.
- Released `sqlite-vec` flat, rescore, and DiskANN capabilities.
- Functional, quality, performance, storage, concurrency, restart, and packaging measurements.

Out of scope:

- KurrentDB Secondary Indexing.
- Schema Registry storage.
- Removal of DuckDB from the KurrentDB distribution.
- A custom build of experimental SQLite IVF.
- Migration of Lance files in place.
- A production rollout before benchmark acceptance.

### Current baseline

The current Lance backend provides:

| Capability | Current implementation |
|---|---|
| Content storage | Lance `memories` and `records` datasets |
| Full text | Lance `INVERTED`, queried by `lance_fts` |
| Vector index | `IVF_HNSW_PQ` |
| Vector metric | Squared L2 |
| IVF partitions | 1 |
| PQ | 8 bits, `num_sub_vectors = embedding dimension` |
| Graph | HNSW `m=16`, `ef_construction=100` |
| Rerank | `refine_factor=4` with full vectors |
| Small collections | Exact scan below the 256-row PQ training floor |
| Hybrid | `lance_hybrid_search`, alpha blend, oversample factor 4 |
| Scalar indexes | `BTREE`; `LABEL_LIST` for tags |
| Maintenance | Append optimize, retrain, compact, and vacuum |

Because `num_partitions=1`, current searches do not prune IVF partitions. The material vector
behavior is HNSW candidate discovery over PQ codes followed by exact reranking.

## Design

### Architecture

```text
KurrentDB source events
        |
        v
Kontext projector/indexer
        |
        | one SQLite transaction per batch
        v
memories.sqlite or records.sqlite
  - primary content table
  - FTS5 external-content table
  - vec0 vector table
  - scalar indexes and normalized tags
  - projection checkpoint
        |
        v
Sqlite memory/record store
  - BM25 query
  - exact or ANN vector query
  - hybrid candidate fusion
        |
        v
Existing Kontext retrieval pipeline
```

Use separate `memories.sqlite` and `records.sqlite` files. Each index has one writer and any number
of WAL readers. This matches the current separation between Lance datasets and avoids serializing
the memories projector and records indexer through one SQLite writer lock.

The implementation must remain behind the existing storage and retrieval abstractions. Backend
selection must be explicit configuration. It must not silently fall back from SQLite to Lance or
from Lance to SQLite after an initialization failure.

### Dependencies

Pin the same SQLite packages already used by KurrentDB:

```xml
<PackageReference Include="Microsoft.Data.Sqlite" />
<PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" />
```

The validated versions are `Microsoft.Data.Sqlite` 10.0.10 and
`SQLitePCLRaw.bundle_e_sqlite3` 2.1.12. The probe loaded SQLite 3.53.3. This version includes the fix
for SQLite's 2026 WAL-reset race.

Vendor the upstream `sqlite-vec` 0.1.10-alpha.4 release binaries during the prototype. Package one
binary for each supported runtime identifier and verify its published SHA-256 checksum during the
build or packaging process. Load it from an application-owned absolute path with
`SqliteConnection.LoadExtension`. Never execute `INSTALL`, download an extension at node startup, or
search arbitrary library paths.

The tested release binary reported these build flags:

```text
neon rescore diskann
```

It did not include experimental IVF.

### Connection configuration

Each writable database must initialize with:

```sql
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA foreign_keys = ON;
PRAGMA busy_timeout = 5000;
```

`synchronous=NORMAL` is acceptable for a derived index only if the checkpoint and index rows commit
in the same transaction and replay repairs a lost final transaction. The prototype must include a
`FULL` durability benchmark so a production choice is explicit.

Open write connections without provider pooling. If read pooling is added, every returned reader
must be closed promptly so it cannot starve WAL checkpoints. Load `sqlite-vec` on each physical
connection before preparing vector statements.

### Storage model

Each primary table is a `STRICT` ordinary SQLite table. Use a named `row_id INTEGER PRIMARY KEY` as
the join key shared by the primary table, FTS5, vectors, and tag rows. A named key can be the target
of foreign keys and remains an alias for SQLite's integer rowid. Preserve the current external memory
or record identifier as a unique column.

The memories database contains:

| Object | Purpose |
|---|---|
| `memories` | Current memory read model without the vector payload |
| `memories_fts` | External-content FTS5 index over `content` |
| `memories_vec` | Vector and scalar vector-prefilter columns |
| `memory_tags` | One normalized row per memory and tag |
| `checkpoints` | Projector resume position |

The records database contains:

| Object | Purpose |
|---|---|
| `records` | Current whole-log record read model |
| `records_fts` | External-content FTS5 index over `content` |
| `records_vec` | Record embedding and scalar prefilters |
| `checkpoints` | Indexer resume position |

Representative memory vector schema:

```sql
CREATE VIRTUAL TABLE memories_vec USING vec0(
    memory_row_id INTEGER PRIMARY KEY,
    embedding FLOAT[384] distance_metric=l2,
    memory_type INTEGER,
    is_retracted BOOLEAN,
    is_superseded BOOLEAN
);
```

The dimension is configuration, not a hardcoded value. It must match the embedding provider and the
normal table's schema metadata. Reject a mismatch during startup.

Model tags relationally:

```sql
CREATE TABLE memory_tags (
    memory_row_id INTEGER NOT NULL REFERENCES memories(row_id) ON DELETE CASCADE,
    tag TEXT NOT NULL,
    PRIMARY KEY (memory_row_id, tag)
) STRICT;

CREATE INDEX memory_tags_by_tag ON memory_tags(tag, memory_row_id);
```

The prototype must test whether `vec0` can efficiently consume the eligible rowid set for all-tags
queries. If not, use a relational exact-distance query for those searches. Do not encode tags into a
delimited string and depend on substring matching.

### FTS5

Use an external-content FTS5 table so the primary content has one authoritative copy:

```sql
CREATE VIRTUAL TABLE memories_fts USING fts5(
    content,
    content='memories',
    content_rowid='row_id',
    tokenize='unicode61 remove_diacritics 2'
);
```

Create insert, delete, and content-update triggers according to the FTS5 external-content contract.
Run `INSERT INTO memories_fts(memories_fts) VALUES ('rebuild')` as an explicit repair operation.
Expose FTS5 `integrity-check` and SQLite `PRAGMA integrity_check` through diagnostics or maintenance.

FTS queries must bind user text as parameters. Kontext currently treats input as plain words rather
than an operator language. The adapter must tokenize and quote terms before constructing the FTS5
query expression so punctuation or reserved FTS operators cannot change query meaning.

SQLite `bm25()` sorts smaller values first and commonly returns negative scores. The adapter must
convert the score to Kontext's larger-is-better diagnostic convention, for example `-bm25(table)`.
Scores remain query-relative and must not be treated as calibrated probabilities.

### Write path

One write transaction must include:

1. Upsert the primary row.
2. Let primary-table triggers update FTS5.
3. Insert or update the vector row.
4. Replace normalized tag rows when applicable.
5. Advance the projection checkpoint.
6. Commit.

SQLite `ON CONFLICT DO UPDATE` works on ordinary tables but not virtual tables. The vector writer
must use explicit `UPDATE`, followed by `INSERT` when no row exists, or a deterministic delete plus
insert. Keep this sequence inside the same transaction.

The validated probe demonstrated that rollback removes the primary row, its FTS5 entries, and its
`vec0` row together. The prototype must extend that test to tags and checkpoints.

Batch prepared statements and reuse them on the dedicated writer connection. Measure transaction
sizes rather than assuming that per-row virtual-table operations scale like the current Lance
appender.

### Query modes

#### Exact vector search

The first implementation uses float `vec0`:

```sql
SELECT m.*, v.distance
FROM memories_vec AS v
JOIN memories AS m ON m.row_id = v.memory_row_id
WHERE v.embedding MATCH $query_embedding
  AND k = $candidate_count
  AND v.is_retracted = 0
  AND v.is_superseded = 0
ORDER BY v.distance
LIMIT $limit;
```

Use squared L2 to match current Lance behavior. If embeddings are L2-normalized, preserve the
current conversion between squared L2 and cosine similarity where consumers require similarity.

Scalar state filters belong in `vec0` metadata columns for the flat index. Multi-tag filters use the
normalized tag relation and an eligible-rowid constraint or the exact relational fallback.

#### Full-text search

Query FTS5, join the primary table, apply active-state and tag filters, order by BM25, and limit.
The query must return the same nullable diagnostic shape as `MemoryHit`: keyword score present,
vector and hybrid scores absent.

#### Hybrid search

Run keyword and vector candidate legs independently and materialize each before ranking. FTS5 does
not allow `bm25()` in every window-function context, so the working query shape is:

```text
materialized BM25 candidates
        +
materialized vector candidates
        |
rank or normalize each leg
        |
union candidate rowids
        |
fuse and order
```

Implement two prototype fusion policies:

| Policy | Purpose |
|---|---|
| Alpha blend | Behavioral comparison with current `lance_hybrid_search`; default alpha 0.5 |
| Reciprocal rank fusion | Scale-independent quality alternative; default rank constant 60 |

Alpha blending must normalize vector and keyword values within their candidate sets before mixing
them. RRF must operate on ranks, not raw BM25 and distance values. Keep fusion policy behind the
existing retrieval configuration and report each leg's original diagnostics.

### Vector index strategy

#### Stage 1: flat float

Flat float is mandatory for the first prototype.

Properties:

- Exact recall.
- No training or maintenance scheduler.
- Scalar metadata prefiltering.
- Linear work in eligible rows times embedding dimension.
- Baseline for every ANN recall measurement.

This stage can be the final design if node-local collections are small enough and latency meets the
acceptance target.

#### Stage 2A: rescore

Create a separate benchmark database with:

```sql
embedding FLOAT[384] distance_metric=l2
    INDEXED BY rescore(quantizer=int8, oversample=8)
```

Also test `quantizer=bit`. Rescore scans every compressed vector, selects `k * oversample`
candidates, then recomputes exact float distance. It lowers scan bandwidth but remains linear.

Rescore does not support metadata or partition columns in 0.1.10-alpha.4. Filtered queries must use
an oversampled candidate set followed by relational filtering, or route to the exact path. Do not
claim filter parity until selective-filter tests prove it.

#### Stage 2B: DiskANN

Create a separate benchmark database with a starting configuration such as:

```sql
embedding FLOAT[384] distance_metric=l2
    INDEXED BY diskann(
        neighbor_quantizer=int8,
        n_neighbors=72,
        search_list_size=128,
        buffer_threshold=256
    )
```

Benchmark binary and int8 neighbor quantizers. DiskANN traverses a disk graph with quantized
neighbor vectors and computes exact distances for visited full-precision candidates. Returned
distances are exact for visited candidates, but graph recall is approximate.

DiskANN does not support metadata or partition columns in the tested release. For filtered search,
the prototype must compare:

- Oversample and post-filter.
- Adaptive oversampling until enough matching rows are found.
- Exact fallback for selective or correctness-sensitive filters.

The exact fallback is required if ANN cannot produce the requested result count. Measure query cost
for filter selectivity of 100%, 10%, 1%, and 0.1%.

#### IVF

Do not enable IVF in the first prototype. The source implementation supports centroid partitions,
`nlist`, `nprobe`, and optional int8 or binary quantization, but the feature is guarded by
`SQLITE_VEC_EXPERIMENTAL_IVF_ENABLE=0` and absent from the upstream precompiled release binary.

Using it would require KurrentDB to build and support a custom experimental native extension. It
still would not reproduce Lance's combined IVF plus HNSW plus PQ topology.

### Direct index comparison

| Current Lance behavior | SQLite prototype mapping | Parity |
|---|---|---|
| Exact scan below training floor | Flat float `vec0` | Functional parity |
| `IVF_HNSW_PQ` | DiskANN | Similar graph ANN, different topology and quantizer |
| PQ candidate distance | Rescore int8/bit or DiskANN neighbor quantizer | Similar purpose, different representation |
| `refine_factor=4` | Rescore oversample or DiskANN exact visited-candidate distance | Similar rerank concept |
| One IVF partition | No partition layer in released path | Similar effective lack of IVF pruning |
| `use_index=false` | Exact flat table or scalar exact-distance fallback | Functional parity |
| Unindexed-tail exact merge | DiskANN buffer scan and merge | Similar freshness behavior; must be stress-tested |
| Lance `INVERTED` BM25 | SQLite FTS5 | Functional parity, ranking must be compared |
| Lance `BTREE` | SQLite ordinary indexes | Functional parity |
| Lance `LABEL_LIST` with current pushdown gap | Normalized tag table plus B-tree | Better relational control; vector integration must be measured |
| One-call Lance hybrid search | Two candidate CTEs plus alpha/RRF fusion | Functional parity with more owned SQL |
| Lance optimize/retrain/compact/vacuum | WAL checkpoints, FTS optimize/rebuild, `VACUUM` | Different and simpler for flat mode |

Exact `IVF_HNSW_PQ` parity is not available in released `sqlite-vec`. Achieving the same topology
would require implementing or adopting another native extension. That work is outside this proposal.

### Components

The prototype should add a side-by-side SQLite implementation, not replace Lance classes in place:

| Component | Responsibility |
|---|---|
| `SqliteKontextDataSource` | Paths, connection creation, pragmas, extension loading, reader leases |
| `SqliteMemorySchema` | Tables, FTS5, triggers, vector table, tags, schema version |
| `SqliteRecordsSchema` | Records equivalents and checkpoint schema |
| `SqliteMemoryWriter` | Transactional memory projection writes |
| `SqliteRecordsWriter` | Transactional whole-log batch writes |
| `SqliteMemoryIndex` | Vector, text, hybrid, get, list, and lineage queries |
| `SqliteMaintenance` | WAL checkpoint, FTS optimize/rebuild, integrity diagnostics, optional vacuum |
| Benchmark harness | Side-by-side Lance and SQLite quality/performance runs |

Keep SQL local to the component that executes it. Bind every value. Only dimensions, identifiers
selected from internal constants, and fixed query clauses may appear in SQL text.

### Configuration

Add an explicit backend option for the prototype:

```yaml
KurrentDB:
  Kontext:
    Storage:
      Backend: Lance # Lance | Sqlite
```

The default remains `Lance` during the experiment. Do not infer the backend from existing files.
Reject startup if files from the selected backend have an unsupported schema version.

Prototype-only tuning options may include vector strategy, rescore oversample, DiskANN graph degree,
DiskANN search-list size, WAL synchronous mode, and hybrid fusion policy. No ANN option becomes a
production configuration until accepted benchmark values exist.

### Packaging and platforms

The prototype must verify release binaries on every supported server platform:

| Platform | Required artifact |
|---|---|
| Linux x64 | `sqlite-vec` loadable Linux x86_64 |
| Linux ARM64 | `sqlite-vec` loadable Linux AArch64 |
| macOS ARM64 | `sqlite-vec` loadable macOS AArch64 |
| Windows x64 | `sqlite-vec` loadable Windows x86_64 |

Intel macOS can be evaluated if KurrentDB still supports it; upstream publishes an x86_64 artifact.
The current Lance vendor set lacks macOS x64, so SQLite may improve that platform story.

### Backup and recovery

Treat `-wal` and `-shm` as live database state. Do not copy only the main database file while a node
is running. Use SQLite's backup API, or complete a checkpoint and take a coordinated file copy.

Because Kontext indexes are derived, the authoritative recovery path is rebuild from KurrentDB
events. The checkpoint must never advance without its corresponding index rows. On integrity failure,
quarantine the database, create a new one, and replay.

### Observability

Record at least:

- Projection/indexer checkpoint lag.
- Batch size, transaction duration, and busy retries.
- WAL size and checkpoint duration.
- FTS, vector, and hybrid query latency by mode.
- Exact versus ANN route and fallback counts.
- ANN candidate count, post-filter survivors, and empty-page retries.
- Database file and vector shadow-table sizes.
- Integrity-check and extension-load failures.

Per-query diagnostics belong at Debug or Verbose level, not Information.

## Alternatives Considered

### Keep DuckDB plus Lance

This remains the production baseline. It already provides one-call hybrid search, richer Lance index
types, and open Lance-format interoperability. It also has known extension-specific failure modes,
tag pushdown gaps, index maintenance, and a more complex native stack. The prototype must beat or
materially simplify this baseline; feature equivalence alone does not justify migration.

### Use SQLite flat KNN only

This is the preferred first implementation and may be the final choice for bounded node-local data.
Its risk is linear query cost at records-index scale.

### Use SQLite rescore as the target

Rescore preserves full vectors and exact final distances while reducing coarse-scan bandwidth. It
still scans the collection and cannot use current metadata filters. Evaluate it after flat KNN, not
instead of the exact baseline.

### Use SQLite DiskANN as the target

DiskANN is the closest released graph ANN option. It is also new, alpha, and incompatible with
`vec0` metadata columns. It requires recall, mutation, crash, and selective-filter evidence before
consideration.

### Compile experimental SQLite IVF

Rejected for the first prototype. It adds a custom native build and support obligation without
matching Lance `IVF_HNSW_PQ`.

### Use SQLite VSS/Faiss

Not selected. `sqlite-vss` is superseded by `sqlite-vec`, has a larger native dependency through
Faiss, and does not improve the ownership or packaging argument.

### Replace all KurrentDB DuckDB usage

Out of scope. Secondary Indexing exposes analytical SQL and Arrow Flight behavior that SQLite does
not replace directly. Schema Registry also has separate requirements. Bundling benefits must not be
claimed unless those systems are addressed independently.

## Edge Cases & Failure Modes

| Case | Required behavior |
|---|---|
| Empty database | Create all schemas and return empty searches without special cases |
| Fewer than `k` matches | Return all matches without retry loops |
| Selective tag filter | Return correct exact results; ANN may fall back to exact |
| Retracted or superseded memory | Exclude before exact ranking where supported; never leak through post-filter paging |
| Embedding dimension mismatch | Fail startup or write with a specific error |
| Duplicate external identifier | Upsert ordinary row and synchronize vector/tags in one transaction |
| Vector virtual-table update failure | Roll back primary row, FTS, tags, and checkpoint |
| Process crash during write | Recover last committed WAL state and replay from checkpoint |
| Long-lived reader | Do not allow unbounded WAL growth; expose checkpoint starvation metrics |
| Concurrent writers | Serialize per database, apply busy timeout, and retry only `SQLITE_BUSY` with bounds |
| Extension missing or wrong architecture | Fail startup; do not continue without vector search |
| Extension ABI/version mismatch | Fail a startup version probe before schema access |
| FTS syntax characters in user text | Quote/tokenize as literal terms; do not expose an operator language accidentally |
| NaN or infinite embedding values | Reject before binding |
| Tag replacement | Delete and insert normalized rows in the parent transaction |
| Database corruption | Stop using the file and rebuild from the event source |
| Schema upgrade | Apply explicit numbered migrations; never rely on `CREATE IF NOT EXISTS` to alter shape |

SQLite allows one writer per database. Separate memory and record files reduce contention but do not
remove contention within each projector. The benchmark must include concurrent search readers,
checkpoint work, and sustained ingestion.

ANN filters are the largest functional risk. Rescore, IVF, and DiskANN in the examined source reject
metadata and partition columns. Oversampling alone cannot guarantee that graph ANN finds every
eligible nearest neighbor. The adapter must retain an exact route and use it when correctness or
selectivity demands it.

## Testing

### Existing probe evidence

The .NET 10 probe has already validated:

- Loading the upstream macOS ARM64 extension through `Microsoft.Data.Sqlite`.
- SQLite 3.53.3, FTS5, WAL, and `sqlite-vec` 0.1.10-alpha.4.
- Float-array BLOB binding.
- BM25 search.
- Exact cosine KNN with scalar metadata filters.
- Hybrid RRF in SQL.
- Transactional rollback across primary row, FTS5, and vector row.
- Vector and FTS update behavior.
- Close/reopen persistence.
- SQLite integrity check.

This evidence proves feasibility, not production scale.

### Functional test matrix

Add integration tests for:

- Schema creation and idempotent startup.
- Insert, update, retract, supersede, delete, and replay.
- FTS trigger consistency after every mutation.
- Vector mutation and dimension validation.
- Multi-tag all-of filtering.
- Point reads, list queries, and recursive lineage.
- Vector-only, FTS-only, alpha-hybrid, and RRF-hybrid result shapes.
- Checkpoint atomicity with row, FTS, vector, and tags.
- Transaction rollback at each write step.
- Restart from committed and rolled-back batches.
- Two readers during sustained writes in WAL mode.
- Busy timeout and bounded retry.
- Backup API restore and full event-log rebuild.
- Unsupported extension version and missing native artifact.
- Every supported runtime identifier in CI.

### Quality benchmark

Use the same corpus, embeddings, filters, and queries for Lance and SQLite. Treat exact flat SQLite
KNN as the vector ground truth. Measure:

- Recall@1, Recall@10, and Recall@50.
- Mean reciprocal rank.
- NDCG@10 for hybrid results.
- Result-count correctness under filters.
- Rank overlap with the current Lance backend.
- Alpha blend versus RRF on the existing Kontext quality corpus.

Run at actual embedding dimension and at 1,000, 10,000, 100,000, and 1,000,000 rows where hardware
permits. Test unfiltered queries and filters with 100%, 10%, 1%, and 0.1% selectivity.

Proposed ANN quality gate:

- Recall@10 at least 0.95 against exact flat KNN.
- No missing requested results when enough eligible rows exist; exact fallback may satisfy this.
- Hybrid NDCG@10 no more than 2% below the current Lance baseline on the agreed corpus.

Management and search owners must approve final thresholds before a production decision.

### Performance benchmark

Measure both cold and warm operation:

- Index build or graph insertion time.
- Sustained projection rows per second.
- Transaction p50, p95, and p99.
- Search p50, p95, and p99 by mode and filter selectivity.
- Concurrent reader throughput during ingestion.
- Peak resident memory.
- Main database, WAL, FTS, and vector shadow-table disk size.
- Startup and crash-recovery time.
- Backup and rebuild time.

Compare exact flat, rescore int8, rescore binary, DiskANN int8, DiskANN binary, and the current Lance
configuration. Use the same machine, corpus order, batch size, and connection concurrency.

### Acceptance criteria

The prototype is complete when:

- All functional tests pass on macOS ARM64 and Linux x64 at minimum.
- Projection replay produces the same logical rows as Lance.
- Exact vector search matches the brute-force ground truth.
- FTS and hybrid quality results are documented against Lance.
- Filtered searches return correct counts and exclude inactive memories.
- A forced rollback leaves content, FTS, vectors, tags, and checkpoint unchanged.
- A crash/restart test resumes without duplicates or gaps.
- Native assets load without network access on each supported RID.
- Performance and storage results identify the row-count boundary where flat KNN stops meeting the
  agreed latency objective.
- ANN results, if proposed, meet the agreed recall and hybrid-quality gates.
- The final report states whether to reject SQLite, retain it as an optional backend, or plan a
  production migration.

## Rollout

### Phase 1: durable probe

Move the isolated .NET probe into the feature's test or benchmark area. Add RID-aware extension
resolution and run it in CI without touching product composition.

### Phase 2: exact side-by-side backend

Implement schemas, writers, readers, checkpoints, and maintenance behind an explicit `Sqlite`
backend selection. Keep `Lance` as the default. Build SQLite indexes by replaying source events.

### Phase 3: parity and quality

Run both backends against the same corpus. Resolve semantic differences in tokenization, BM25 score
direction, squared L2, filtering, hybrid normalization, and pagination. Do not tune ANN during this
phase.

### Phase 4: scale experiments

Benchmark flat float. If it meets targets, stop. If it does not, benchmark rescore and DiskANN in
separate databases and configurations. Keep exact fallback for filtered search.

### Phase 5: decision report

Present capability parity, quality, latency, ingestion, memory, disk, packaging, operational work,
and upstream maturity. Recommend one of:

1. Keep Lance only.
2. Keep SQLite as an experimental or small-dataset backend.
3. Adopt SQLite exact flat for Kontext.
4. Adopt SQLite with a specified ANN mode and exact fallback.

### Migration and rollback

Do not convert Lance files. Create SQLite files from the event log while Lance remains available.
Switch reads only after the SQLite checkpoint reaches the required source position and parity checks
pass.

Rollback selects the Lance backend and discards the derived SQLite files. No source events or public
data formats change, so backend rollback requires no reverse data migration.

### Management case

Potential benefits:

- Reuses a database provider already shipped by KurrentDB.
- FTS5 is mature, transactional, and maintained with SQLite.
- One database transaction can cover content, text index, vector row, tags, and checkpoint.
- Flat KNN removes vector-index training, append optimization, retraining, compaction, and stale
  Lance-handle recovery.
- SQLite files have established integrity, backup, WAL, and diagnostic tooling.
- The backend remains a rebuildable local read model.

Material risks:

- `sqlite-vec` is pre-1.0 alpha software.
- There is no first-party .NET package for its native binary.
- Released ANN modes cannot use metadata or partition columns.
- Exact KNN may not meet records-index scale latency.
- DiskANN mutation, recall, and operational behavior are not yet proven for KurrentDB.
- SQLite hybrid fusion becomes code that KurrentDB owns.
- DuckDB remains elsewhere in KurrentDB, so this does not remove the product-level DuckDB dependency.

The option is credible because the core functional path has run successfully through .NET, not
because SQLite is assumed to be simpler. The benchmark must show whether the measured simplicity is
worth the search and dependency risks.

## References

Repository evidence:

- Current memory schema and `IVF_HNSW_PQ` configuration:
  `src/Kontext/Kurrent.Kontext/KontextSchema.cs`.
- Current records schema and matching vector configuration:
  `src/Kontext/Kurrent.Kontext/Modules/Records/Indexer/KontextRecordsSchema.cs`.
- Current vector, full-text, and hybrid SQL:
  `src/Kontext/Kurrent.Kontext/Modules/Memory/Data/KontextDataStore.cs`.
- Validated Lance behavior and index lifecycle: [`project/duckdb-lance.md`](../../../../project/duckdb-lance.md).
- Current records-indexer decisions: [Kontext Records Indexer](../../2026-08-10-1619-kontext-records-indexer/design/design.md).
- Earlier Lance feasibility report:
  [DuckDB + LanceDB for Kontext Hybrid Search](../../../reports/2026-07-08-duckdb-lancedb-hybrid-search/report.md).

Upstream references:

- SQLite FTS5: <https://www.sqlite.org/fts5.html>.
- SQLite WAL: <https://www.sqlite.org/wal.html>.
- Microsoft.Data.Sqlite extension loading:
  <https://learn.microsoft.com/dotnet/standard/data/sqlite/extensions>.
- `sqlite-vec` documentation: <https://alexgarcia.xyz/sqlite-vec/>.
- `sqlite-vec` source and releases: <https://github.com/asg017/sqlite-vec>.

Probe evidence, 2026-08-13:

- .NET SDK 10.0.301.
- `Microsoft.Data.Sqlite` 10.0.10.
- `SQLitePCLRaw.bundle_e_sqlite3` 2.1.12.
- SQLite 3.53.3 with FTS5.
- `sqlite-vec` 0.1.10-alpha.4, commit `04d28bd21773981e2d266bbf6aa4efbd011eb4f6`.
- Upstream precompiled macOS ARM64 binary with `neon rescore diskann` build flags.
- Final result: all defined functional probes passed.
