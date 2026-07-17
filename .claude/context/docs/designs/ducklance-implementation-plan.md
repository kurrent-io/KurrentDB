# DuckLance — staged implementation plan

| | |
|---|---|
| **Status** | Ready to execute — all pre-implementation validation done (see `ducklance-validation-report.md`) |
| **Date** | 2026-07-17 |
| **Spec** | `duckdb-connector-spec.md` (validated & corrected); facts: `duckdb-lancedb-reference.md` |
| **Repo target** | `kurrentdb` — new projects `src/Kurrent.SemanticKernel.Connectors.DuckLance` (+ `.Tests`), wired into `KurrentDB.slnx` |
| **Consumer** | `Kurrent.Kontext` (`KontextMemory`) — already programs against `Microsoft.Extensions.VectorData.Abstractions` 10.x and today runs on a hand-written `TestVectorStore` test double whose own comment notes *no public connector implements MEVD 10.x yet*. DuckLance is that connector. |
| **End goal (acceptance)** | `KontextMemory` integration tests pass against DuckLance instead of `TestVectorStore`; Lucene + usearch path retired for Kontext memory |

Effort markers: **S** ≈ hours, **M** ≈ a day-ish, **L** ≈ a few days. Two-lane parallelization (e.g. Sérgio + William) marked per stage. Every stage lands with its tests (TUnit; unit vs integration split per spec §12) — nothing is "tests later".

---

## Milestone 0 — Foundations (Stages 0–2)

### Stage 0 — Scaffolding & CI (S/M) — do first, together
- Create `src/Kurrent.SemanticKernel.Connectors.DuckLance/` (`net10.0`, nullable, implicit usings — match `Kurrent.Kontext.csproj` conventions) and `src/Kurrent.SemanticKernel.Connectors.DuckLance.Tests/`; add both to `KurrentDB.slnx`.
- Pin dependencies exactly (spec §7) where the repo pins versions: `DuckDB.NET.Data.Full` 1.5.3, `Microsoft.Extensions.VectorData.Abstractions` 10.7.0 (align with the version `Kurrent.Kontext` already uses), `Microsoft.Extensions.AI.Abstractions`, `Kurrent.Quack`, `TUnit`.
- Folder layout per spec §6 namespaces: `Schema/`, `Mapping/`, `Filtering/`, `Indexing/`, `Search/`, `Storage/`, `Embeddings/`.
- CI: one smoke test that opens DuckDB, `INSTALL lance; LOAD lance;`, checks the version. Decide extension delivery for CI (network install with cache vs. pre-downloaded `.duckdb_extension` via the `ExtensionPath` option — needed anyway for air-gapped). Gate integration tests on supported platforms (linux x64/arm64, osx arm64, win x64) with a `[LanceRequired]`-style skip attribute.
- **Done when:** empty projects build in CI; the lance smoke test is green on the CI platform.

### Stage 1 — Model & schema layer (M) — *lane A; no DB needed, fully unit-testable*
- `DuckDBModelReader` (attributes or `VectorStoreCollectionDefinition` as sole source of truth), `DuckDBPropertyModel`, `DuckDBSchemaValidator` (dimension, vector CLR types, string-only keys, Lance naming rule for `{StoreKey}_{collection}`), `DuckDBSchemaBuilder` (columns: `FLOAT[dim]`, `VARCHAR[]`, etc.), exception types per §8.8.
- **Done when:** §12 unit list for the schema layer passes (bad dimension / bad key type / bad vector type all rejected at construction; storage-name overrides honored).

### Stage 2 — Storage plumbing (M) — *lane B; first real-DB code*
- `DuckDBConnectionManager`: wraps `Kurrent.Quack` pooling (pooling only — plain SQL everywhere else); extension bootstrap (`INSTALL`/`LOAD`, `ExtensionPath` fallback); sync-under-`Task.Run` (DuckDB has no true async); **recycle-connection-and-retry on `LanceError(IO): Not found`** (validated cache-staleness defense, Decision #23).
- `DuckDBDatasetResolver`: single `ATTACH '{StoragePath}'` per store (validated: missing dir is fine — created lazily by first `CREATE TABLE`); table name `{StoreKey}_{collection}` for DML **and** search (validated: search functions accept attached names); raw dataset path for index DDL only.
- `DuckDBVectorStore` skeleton + `GetCollection` (internal collection ctor per spec §5).
- **Done when:** integration test attaches a temp store, creates/drops a table via the resolver, and survives a forced connection recycle.

---

## Milestone 1 — CRUD end-to-end (Stages 3–4) → *first demo: records in, records out*

### Stage 3 — Collection lifecycle (S) — depends on 1 + 2
- `EnsureCollectionExistsAsync` (`CREATE TABLE IF NOT EXISTS` from the schema builder), `CollectionExistsAsync`, `EnsureCollectionDeletedAsync` (`DROP TABLE` — validated to remove the dataset from disk), `ListCollectionNamesAsync` (`duckdb_tables()` filtered to the attached catalog), schema-drift → throw (§8.7).
- **Done when:** lifecycle BDD scenarios pass, including two stores sharing one `StoragePath` with different `StoreKey`s not colliding.

### Stage 4 — Mappers + CRUD (M/L)
- `DuckDBRecordMapper` (POCO) first; dynamic (`Dictionary<string, object?>`) mapper second. Read-back types are `List<float>`/`List<string>` (validated) — map to `ReadOnlyMemory<float>`/`Embedding<float>`/`float[]` and collection properties.
- `UpsertAsync` single = validated `MERGE INTO ... USING (SELECT ? ...)` with `CAST(? AS FLOAT[dim])`/`CAST(? AS VARCHAR[])`; batch = multi-row `USING (VALUES ...)` (fewer Lance commits = fewer conflict windows, §13.13 rationale — fine to land serial-batch first and optimize in Stage 11).
- `GetAsync` (key, keys), `DeleteAsync` (key, keys — missing keys silently fine, validated).
- **Done when:** all Upsert/Delete BDD scenarios pass against a real temp dataset, round-tripping vectors + tags exactly.

---

## Milestone 2 — Search (Stages 5–7) → *second demo: semantic search over Kontext-shaped records*

### Stage 5 — Vector search (M) — *lane A*
- `SearchAsync` via `lance_vector_search('{ns}.main.{table}', 'vec', CAST(? AS FLOAT[dim]), k := top+skip, prefilter := true, refine_factor := 4)` — query vector always parameter-bound (validated).
- `DuckDBScoreConverter` per corrected Decision #1: `CosineDistance`/`EuclideanSquaredDistance` → raw `_distance`; `CosineSimilarity`/`DotProductSimilarity` → `1 − _distance`; plain `EuclideanDistance` → `NotSupportedException`.
- **`refine_factor` always set when the index kind is PQ-family** (Decision #22 — without it, small-k results are wrong).
- `Top`/`Skip` (fetch `top+skip`, `LIMIT/OFFSET`), `IncludeVectors`, empty-collection returns empty (validated w/ `train=false`-style flows).
- **Done when:** distance-formula tests reproduce the validated 0/1/2 (cosine) and 0/2/4 (squared-L2) numbers; paging scenarios pass.

### Stage 6 — Filtering (M) — *lane A, after 5*
- `DuckDBFilterTranslator`: exactly two shapes (§8.5). Equality → parameterized `WHERE` + `prefilter := true` (validated true-prefilter semantics, bound params push down). Containment → **oversample path**: large `k`, DuckDB evaluates `array_has_any`, `LIMIT top` (validated correct; the extension's filter IR can't push containment — Decision #21). `&&` combines; a containment clause switches the whole query to the oversample path. Anything else → `NotSupportedException` at translation time, hard boundary.
- **Done when:** filter BDD scenarios pass, including the "matching rows far outside global top-k" case (the one that catches silent post-filtering) and unsupported-shape throws.

### Stage 7 — Indexing (M) — *lane B, parallel with 5–6*
- `DuckDBIndexKindMapper` (§8.4 table: `IvfFlat`→`IVF_FLAT`, `QuantizedFlat`/`Dynamic`→`IVF_PQ`, `Hnsw`→`IVF_HNSW_PQ`, `DiskAnn`→throw), `DuckDBIndexBuilder` (`CREATE INDEX ... USING ... WITH (...)` — **`=` inside `WITH`, never `:=`**; `num_sub_vectors`-divides-dimension pre-check), scalar indexes for `IsIndexed` (BTREE) and tags (LABEL_LIST), INVERTED for `IsFullTextIndexed`; wire into `EnsureCollectionExistsAsync`; `SHOW INDEXES` parsing (columns validated: `index_name,index_type,fields,rows_indexed,details`).
- **Done when:** every IndexKind mapping is verified via `SHOW INDEXES` on a real dataset (all six index types were validated live — this stage is mechanical).

---

## Milestone 3 — Full feature set (Stages 8–10)

### Stage 8 — Embedding generation (S) — *lane A*
- `DuckDBEmbeddingGenerationHandler`: `IEmbeddingGenerator` at upsert + search time (store-level option; property/definition level comes free from the abstraction, §8.3). Kontext already has generators (`Kurrent.Kontext.Embeddings`) to test against.
- **Done when:** raw-text upsert + raw-text search scenarios pass with a test-double generator.

### Stage 9 — Hybrid search (S/M) — *lane A, after 8*
- `IKeywordHybridSearchable` via `lance_hybrid_search(uri, 'vec', CAST(? AS FLOAT[dim]), 'content', ?, k := ..., prefilter := true, alpha := ..., refine_factor := 4)`; keywords joined to one query string; `AdditionalProperty` auto-selection; filters via `WHERE` (validated — hybrid has **no** filter parameter, even upstream). Close the one micro-gap: confirm the text argument binds as a parameter (vector binding validated; text was literal in the spike).
- **Done when:** hybrid BDD scenario passes; blended ranking sanity-checked against the validated spike numbers.

### Stage 10 — Reindex scheduler + maintenance policy (M/L) — *lane B, parallel with 8–9*
- `DuckDBReindexScheduler` per §8.9 (in the lib — decided): per-dataset registry on the store; query-driven ticks (`SHOW INDEXES` + `COUNT(*)` → `unindexed_ratio`; threshold → `ALTER INDEX ... OPTIMIZE WITH (mode = 'append')`); compaction as a separate `OPTIMIZE` call; time-based retrain (stored timestamp — confirmed necessary, no timestamp in `SHOW INDEXES`); public `OptimizeIndexesAsync()`.
- **`VACUUM LANCE` with conservative retention only** (days — Decision #23; the validated failure mode requires vacuum deleting files live handles reference). Scheduler unit tests against faked stats; integration tests reproduce the validated append (`rows_indexed` catches up) and the staleness arithmetic.
- **Done when:** reindexing BDD scenarios pass, including "one scheduler per dataset across repeated GetCollection calls".

---

## Milestone 4 — Production-ready (Stages 11–12)

### Stage 11 — Hardening + remaining validations (M)
- Native multi-row batch Get/Upsert/Delete (replace serial wrappers). Error mapping sweep (§8.8): everything DuckDB/Lance → `VectorStoreOperationException` with inner preserved.
- Close the §15 "still open" list as tests: cross-process concurrent writers (two processes, same dataset); scalar type matrix (bool/int widths/decimal/timestamp/DateTimeOffset columns); multi-vector-per-record through the extension; `num_sub_vectors` divisibility error surface.
- **Done when:** §15's open list is empty or consciously waived, each with a test or a written waiver.

### Stage 12 — DI, docs, Kontext integration (M) — *the payoff*
- `ServiceCollectionExtensions` (current MEVD convention: `AddDuckLanceVectorStore` / `AddKeyed...`, `ServiceLifetime` param, keyed services — no `IKernelBuilder`, no `serviceId`).
- **Swap `KontextMemory` onto DuckLance**: an integration variant of `KontextMemoryTests` running against DuckLance instead of `TestVectorStore` (that test double's own comment is the reason this connector exists — retire it from the integration path, keep it for pure-unit speed if useful).
- README + usage sample; decision-log links.
- **Done when:** Kontext memory integration suite is green on DuckLance. That's the finish line for track 1.

---

## Sequencing at a glance

```
Stage 0 ──┬─ lane A: 1 ──► 4 ──► 5 ──► 6 ──► 8 ──► 9 ──┐
          └─ lane B: 2 ──► 3 ──┘      7 ──────► 10 ────┼──► 11 ──► 12
                            (4 needs 1+2+3; 9 needs 7's indexes for FTS)
```

Milestone demos: **M1** = CRUD round-trip · **M2** = filtered semantic search · **M3** = hybrid search + self-maintaining indexes · **M4** = KontextMemory on DuckLance.

## Track 2 (later, unchanged)

Port to microsoft/semantic-kernel conventions: xUnit conformance suites, 4 TFMs, public collection ctors + `DuckDBCollectionOptions`, scheduler extracted to a hosted service, broader key types. One new wrinkle discovered today: the SK repo's connectors currently build against the 9.x-era MEVD API (that's why Kontext needed its own test double) — the port targets whatever MEVD version SK is on when we go; open the maintainer issue (name, package ID) before starting.
