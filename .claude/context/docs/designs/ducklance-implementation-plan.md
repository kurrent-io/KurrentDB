# DuckLance — staged implementation plan

| | |
|---|---|
| **Status** | Ready to execute — all pre-implementation validation done (see `/Users/sergio/dev/kurrent/kurrentdb/.claude/context/docs/designs/ducklance-validation-report.md`) |
| **Date** | 2026-07-17 |
| **Spec** | `/Users/sergio/dev/kurrent/kurrentdb/.claude/context/docs/designs/duckdb-connector-spec.md` (validated & corrected); facts: `/Users/sergio/dev/kurrent/kurrentdb/.claude/context/docs/designs/duckdb-lancedb-reference.md` |
| **Repo target** | **(revised 2026-07-17)** Sérgio's fork of microsoft/semantic-kernel at `/Users/sergio/dev/contrib/semantic-kernel` — new projects `/Users/sergio/dev/contrib/semantic-kernel/dotnet/src/VectorData/DuckLance` + `/Users/sergio/dev/contrib/semantic-kernel/dotnet/test/VectorData/DuckLance.Tests`, wired into `/Users/sergio/dev/contrib/semantic-kernel/dotnet/MEVD.slnx`. kurrentdb consumes the built package via `dotnet pack` → a local NuGet feed (nothing is published publicly). |
| **Consumer** | `Kurrent.Kontext` (`/Users/sergio/dev/kurrent/kurrentdb/src/Kurrent.Kontext/KontextMemory.cs`) — already programs against `Microsoft.Extensions.VectorData.Abstractions` 10.x and today runs on a hand-written `TestVectorStore` test double whose own comment notes *no public connector implements MEVD 10.x yet*. DuckLance is that connector. |
| **End goal (acceptance)** | `KontextMemory` integration tests pass against DuckLance instead of `TestVectorStore`; Lucene + usearch path retired for Kontext memory |
| **Execution model** | Orchestrator owns contracts + seams + verification; all coding/research/grunt work delegated as self-contained packets (agents or humans) — see *Execution & delegation model* |

## One-shot kickoff (start here in a fresh session)

This plan + the three sibling docs are designed to be the **complete context** — a fresh session has no conversation history and no cross-repo agent memory, so everything needed is in this folder. Kickoff prompt for a new session started in `/Users/sergio/dev/contrib/semantic-kernel` (the SK fork — where the code lives):

> Implement DuckLance following `/Users/sergio/dev/kurrent/kurrentdb/.claude/context/docs/designs/ducklance-implementation-plan.md`. Read it fully first, then its three siblings: `/Users/sergio/dev/kurrent/kurrentdb/.claude/context/docs/designs/duckdb-connector-spec.md` (the design — every "validated 2026-07-17" marker is a live-tested fact), `/Users/sergio/dev/kurrent/kurrentdb/.claude/context/docs/designs/duckdb-lancedb-reference.md` (Lance/DuckDB fact sheet), `/Users/sergio/dev/kurrent/kurrentdb/.claude/context/docs/designs/ducklance-validation-report.md` (golden SQL oracles). Use the delegation model in the plan. Start at Stage 0.

Ground truths a fresh session must know (all in-repo, none in anyone's memory):

- **MEVD 10.x API ground truth (revised 2026-07-17):** the SK fork's own pin is `Microsoft.Extensions.VectorData.Abstractions` **10.1.0** + `Microsoft.Extensions.AI.Abstractions` **10.5.0** (`/Users/sergio/dev/contrib/semantic-kernel/dotnet/Directory.Packages.props` lines ~129/155 — verified), so the **in-tree sibling connector sources** (`/Users/sergio/dev/contrib/semantic-kernel/dotnet/src/VectorData/SqliteVec/`, `PgVector/`, `SqlServer/`) are valid current-API architecture exemplars. (Earlier drafts warned SK connectors were 9.x-era — that's true of the *published packages*, not this checkout's source.) `/Users/sergio/dev/kurrent/kurrentdb/src/Kurrent.Kontext.Tests/TestVectorStore.cs` remains the consumer-side exemplar, and `/Users/sergio/dev/kurrent/kurrentdb/src/Kurrent.Kontext/Kurrent.Kontext.csproj` pins Kontext's target (10.7.0). **Stage 0 resolves the 10.1.0-vs-10.7.0 delta** — default: `VersionOverride="10.7.0"` (+ matching ME.AI) on the two DuckLance projects only (CPM overrides are not disabled in this repo), leaving the rest of the fork untouched; fallback is building against the repo's 10.1.0 if no 10.7-only API is needed.
- **Dependency rules (HARD CONSTRAINT):** DuckLance and its test project reference **NuGet packages only** — `DuckDB.NET.Data.Full`, `Microsoft.Extensions.VectorData.Abstractions`, `Microsoft.Extensions.AI.Abstractions`, `Kurrent.Quack` (a NuGet package — **used for the connection pool and nothing else**; do not pull in `Kurrent.Quack.Arrow` or any other Quack facility), `TUnit`. **Zero `ProjectReference`s to any `KurrentDB.*` or `Kurrent.Kontext.*` project, ever.** Dependency direction is one-way: Kontext consumes DuckLance (Stage 12 adds that reference *in Kontext's test project*); DuckLance never references anything in this repo. Every repo file path named in this plan (`TestVectorStore.cs`, the csprojs below, etc.) is a **read-only exemplar** — something to open and learn from, never something to link against.
- **`Kurrent.Quack` API examples (read-only):** `/Users/sergio/dev/kurrent/kurrentdb/src/KurrentDB.Core/KurrentDB.Core.csproj`, `/Users/sergio/dev/kurrent/kurrentdb/src/KurrentDB.SecondaryIndexing/KurrentDB.SecondaryIndexing.csproj`, and `/Users/sergio/dev/kurrent/kurrentdb/src/SchemaRegistry/KurrentDB.SchemaRegistry/KurrentDB.SchemaRegistry.csproj` (and their code) show how the repo consumes the package — for learning the pool API only, per the dependency rules above.
- **Version pinning:** the SK fork uses Central Package Management — `/Users/sergio/dev/contrib/semantic-kernel/dotnet/Directory.Packages.props`. `DuckDB.NET.Data.Full` is already pinned there at **1.2.0** (line ~174, a legacy leftover) → **bump to 1.5.3**; add `Kurrent.Quack` and `TUnit` entries. Pins are exact versions, policy per spec §7. (kurrentdb-side pinning only matters at Stage 12, when Kontext adds the packed DuckLance package.)
- **Verification oracles:** the spike harness in `/Users/sergio/dev/kurrent/kurrentdb/.claude/context/docs/designs/lancespike/` reruns the entire 56-check validation (`dotnet run`; needs network once for `INSTALL lance`; Apple Silicon / Linux x64/arm64 / Windows x64 only). Its output strings are the golden oracles the delegation map refers to.
- **Known stale-doc trap:** any spec text *not* carrying a "validated/corrected 2026-07-17" marker predates the live validation — if it contradicts a marked section, the marked section wins.

---

Effort markers: **S** ≈ hours, **M** ≈ a day-ish, **L** ≈ a few days. Two-lane parallelization (e.g. Sérgio + William) marked per stage. Every stage lands with its tests (TUnit; unit vs integration split per spec §12) — nothing is "tests later".

**Execution model:** an orchestrator (top-tier model or senior human) owns the contracts and integration seams; **all coding, research, and grunt work is delegated** to sub-agents or teammates as self-contained packets — see *Execution & delegation model* below. The spec's component decomposition (§6) plus the validation run's golden outputs make most components independently buildable and independently verifiable.

---

## Execution & delegation model

Two structural facts make this plan unusually delegable: the spec already decomposed the connector into thin components with written contracts (§6), and the validation run produced **golden oracles** — exact SQL strings, exact distance numbers, exact error behaviors that we *know* are correct. A packet = one component + its contract + its oracle + a definition of done. Whoever executes it (agent or human) never needs the full picture, and the reviewer never needs to trust — only to check against the oracle.

### Ground rules

1. **The orchestrator never writes packet code; it writes contracts and verifies results.** Reserved for the orchestrator: freezing the `DuckDBPropertyModel` contract (everything consumes it — one author, then frozen), `DuckDBCollection`/`DuckDBVectorStore` orchestration, stage wiring/integration seams, and the decision when reality diverges from spec.
2. **Every packet brief contains four things:** (a) contract — the spec §§ and reference-doc facts it implements; (b) oracle — the golden output or test list it must satisfy; (c) definition of done — which tests exist and pass; (d) blast radius — the exact files it may touch, nothing else.
3. **Nothing merges unverified.** Every packet gets a verification pass against its oracle by the orchestrator or a second, independent agent; logic-heavy packets (translator, composers, scheduler, mappers) additionally get an adversarial review ("try to construct an input where this emits wrong SQL / wrong result").
4. **Research is delegated too** — e.g. "check whether a new lance-duckdb release makes `filter :=` usable for local datasets" or "sweep the DuckDB↔Arrow type matrix" are sub-agent tasks with a written-findings deliverable, same verification discipline.

### Executor tiers (best-judgment guide, not law)

| Tier | Use for | In this plan |
|---|---|---|
| **Orchestrator** (top-tier / senior) | contracts, seams, verification, divergence calls | property-model freeze; collection/store orchestration; every merge gate |
| **Opus-class** | high-judgment coding: subtle invariants, error semantics, expression trees | filter translator, search SQL composer, record mappers, connection manager, scheduler, Kontext test parameterization |
| **Sonnet-class** | well-specified standard coding with a clear oracle | schema builder, index mapper/builder, upsert composer, lifecycle, DI, embedding handler, CI plumbing, §15 validation sweeps |
| **Haiku-class** | grunt work: boilerplate, extraction, formatting | scaffolding/csproj/slnx wiring, golden-string extraction from the spike, BDD-name → test-stub generation, README/doc formatting |

### Delegation map (the packets)

| Packet | Stage | Contract (inputs) | Oracle / verification | Executor |
|---|---|---|---|---|
| Project scaffolding, slnx wiring, dependency pins | 0 | plan Stage 0; `/Users/sergio/dev/contrib/semantic-kernel/dotnet/src/VectorData/SqliteVec/SqliteVec.csproj` as structural reference (adapted: net10-only, TUnit, no nuget-package.props import) | builds green | Haiku |
| CI lance smoke, `[LanceRequired]` gate, extension delivery (`ExtensionPath`) | 0 | reference doc (platforms, install) | smoke green on CI platform | Sonnet |
| **Property model + model reader + schema validator** | 1 | spec §8.1/§6; MEVD 10.x API | §12 unit list (bad dim/key/type rejected) | **contract by orchestrator**, implementation Opus |
| Schema builder (column DDL) | 1 | frozen property model | golden DDL from spike | Sonnet |
| Options classes + exception types | 1 | spec §6/§8.8 | spec tables | Haiku/Sonnet |
| Connection manager (Quack wrap, bootstrap, sync-wrap, recycle-on-`IO: Not found`) | 2 | spec §6; Decision #23 | integration smoke + forced-recycle test | Opus |
| Dataset resolver + store skeleton | 2 | spec §5 (validated: lazy dir, attached-name search) | attach/create/drop integration | Sonnet |
| Lifecycle manager | 3 | spec §8.7 | lifecycle BDD incl. StoreKey collision | Sonnet |
| Record mappers (POCO + dynamic) | 4 | frozen property model; validated read-back types (`List<float>`/`List<string>`) | fake-`DbDataReader` unit + Stage-4 round-trip | Opus |
| Upsert/MERGE SQL composer (single + multi-row) | 4 | validated MERGE statement | golden SQL | Sonnet |
| **Search SQL composer** (vector/fts/hybrid; `refine_factor` rule, `prefilter` always explicit, oversample path, k = top+skip) | 5/6/9 | validation-report cheat-sheet; Decisions #21/#22 | golden SQL strings, verbatim | Opus |
| Score converter | 5 | Decision #1 mapping | the 0/1/2 and 0/2/4 numbers | Haiku |
| **Filter translator** (two shapes + hard `NotSupportedException` boundary) | 6 | spec §8.5 | shape table + boundary cases; adversarial review | Opus |
| Index-kind mapper + index builder (incl. divisibility pre-check, `=`-in-`WITH`) | 7 | spec §8.4 (validated) | `SHOW INDEXES` integration per kind | Sonnet |
| Embedding generation handler | 8 | spec §8.3 (standard `IEmbeddingGenerator` contract; SK-provided generator via NuGet for tests — dependency rules) | upsert/search-with-generation tests using an SK generator | Sonnet |
| **Reindex scheduler** (trigger logic behind faked `IIndexStatsSource`, timer, retrain timestamp, vacuum policy) | 10 | spec §8.9; Decision #23 | faked-stats unit + validated-append integration | Opus |
| Batch-native Get/Upsert/Delete | 11 | spec §13.13 (commit-count rationale) | round-trip + commit-count check | Sonnet |
| §15 open-item validation sweeps (cross-process writers, type matrix, multi-vector via extension, hybrid text-param binding) | 11 | reference doc; spike harness as template | new spike checks, findings written back to reference doc | Sonnet (research packets) |
| DI extensions (`AddDuckLanceVectorStore` / keyed, MEVD convention) | 12 | memory/plan DI notes | DI unit tests | Sonnet |
| `KontextMemoryTests` parameterization (run against any `VectorStore`) | 12 — **prep any time** | `/Users/sergio/dev/kurrent/kurrentdb/src/Kurrent.Kontext.Tests/KontextMemoryTests.cs` + `/Users/sergio/dev/kurrent/kurrentdb/src/Kurrent.Kontext.Tests/TestVectorStore.cs` | suite stays green on `TestVectorStore` first, then DuckLance | Opus |
| README, usage sample, doc links | 12 | all design docs | orchestrator review | Haiku |

**Not delegable** (orchestrator-only): the property-model contract itself, `DuckDBCollection`/`DuckDBVectorStore` orchestration, stage integration/wiring, and merge-gate verification. These are the seams where contract drift would silently corrupt every downstream packet.

---

## Milestone 0 — Foundations (Stages 0–2)

### Stage 0 — Scaffolding & CI (S/M) — do first, together
- Create `/Users/sergio/dev/contrib/semantic-kernel/dotnet/src/VectorData/DuckLance/DuckLance.csproj` and `/Users/sergio/dev/contrib/semantic-kernel/dotnet/test/VectorData/DuckLance.Tests/DuckLance.Tests.csproj`; add both to `/Users/sergio/dev/contrib/semantic-kernel/dotnet/MEVD.slnx` (under the existing `/src/VectorData/` and `/test/VectorData/` solution folders). csproj shape: `AssemblyName`/`PackageId` = `Kurrent.SemanticKernel.Connectors.DuckLance`, `net10.0` only, TUnit, `IsPackable=true` (local-feed consumption) — do **not** import the repo's `nuget-package.props` (that's the Microsoft publishing pipeline); the projects inherit `dotnet/src/VectorData/Directory.Build.props` automatically (which usefully NoWarns `MEVD9000/9001` experimental APIs). Use `/Users/sergio/dev/contrib/semantic-kernel/dotnet/src/VectorData/SqliteVec/SqliteVec.csproj` as a structural reference only — adapted per the above.
- Pin dependencies exactly (spec §7) in the fork's `Directory.Packages.props`: bump `DuckDB.NET.Data.Full` 1.2.0 → 1.5.3; add `Kurrent.Quack` + `TUnit`; resolve MEVD 10.1.0 vs 10.7.0 per the kickoff ground truth (default: per-project `VersionOverride` to 10.7.0 + matching `Microsoft.Extensions.AI.Abstractions`).
- Folder layout per spec §6 namespaces: `Schema/`, `Mapping/`, `Filtering/`, `Indexing/`, `Search/`, `Storage/`, `Embeddings/`.
- Verification loop: one smoke test that opens DuckDB, `INSTALL lance; LOAD lance;`, checks the version — run via local `dotnet test` (the fork's GitHub workflows would auto-glob a `*Tests.csproj` if ever enabled, but local is the loop that matters). Decide extension delivery (network install with cache vs. pre-downloaded `.duckdb_extension` via the `ExtensionPath` option — needed anyway for air-gapped). Gate integration tests on supported platforms (linux x64/arm64, osx arm64, win x64) with a `[LanceRequired]`-style skip attribute.
- **Done when:** both projects build; the lance smoke test is green locally (`dotnet test`).

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
- `DuckDBEmbeddingGenerationHandler`: wires the standard `IEmbeddingGenerator` contract at upsert + search time (store-level option; property/definition level comes free from the abstraction, §8.3). The connector never implements or fakes embedding generation — it accepts whatever `IEmbeddingGenerator` the caller supplies, exactly as the abstraction defines. Tests use a **real SK-provided generator via NuGet** (e.g. Semantic Kernel's ONNX Bert embedding generator, `Microsoft.SemanticKernel.Connectors.Onnx`, with a small local model — no API keys, CI-cacheable), which satisfies the dependency rules (packages only; no `Kurrent.Kontext.Embeddings` reference — Kontext consumes the same SK packages).
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
- **Swap `KontextMemory` onto DuckLance** (this work lives in *Kontext's* projects, in the kurrentdb repo — `dotnet pack` DuckLance from the SK fork into a local NuGet feed, then Kontext adds the `Kurrent.SemanticKernel.Connectors.DuckLance` package; Kontext gains a reference to DuckLance, never the reverse): an integration variant of `KontextMemoryTests` (`/Users/sergio/dev/kurrent/kurrentdb/src/Kurrent.Kontext.Tests/KontextMemoryTests.cs`) running against DuckLance instead of `TestVectorStore` (`/Users/sergio/dev/kurrent/kurrentdb/src/Kurrent.Kontext.Tests/TestVectorStore.cs` — that test double's own comment is the reason this connector exists; retire it from the integration path, keep it for pure-unit speed if useful).
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
