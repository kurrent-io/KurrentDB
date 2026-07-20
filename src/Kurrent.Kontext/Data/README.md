# KontextStore

A self-contained **DuckDB + Lance** memory store that speaks the Kontext MCP contracts natively.
One source file (`KontextStore.cs`) plus this README. No `Microsoft.Extensions.VectorData`, no
connector abstraction — the MCP model types in `Kurrent.Kontext.Mcp.Model` **are** the contract.

```
new KontextStore(storagePath, embedder, dimensions: 384)
```

## What it does

| Operation | Method | Behaviour |
| --- | --- | --- |
| **Retain** | `RetainAsync(Memory, relatedTop)` | Upserts a memory (generates `mem-…` id when absent), returns the id plus the most-similar existing memories (related-memory discovery). When the memory `Supersedes` others, those rows are stamped `superseded_at` / `superseded_by`. |
| **Recall** | `RecallAsync(query, RecallOptions)` | Hybrid search — blends vector similarity and BM25 keyword relevance. Honours scoped-tag filters, `MinScore`, and a **lean** vs **full** projection (`IncludeFull`). Excludes retracted memories. |
| **Recollect** | `RecollectAsync(RecollectOptions)` | Plain filtered listing (no relevance): filter by memory `Types` and `Tags`, sort by `RetainedAt` / `LastAccessedAt` / `Importance` in either direction. Excludes retracted memories. |
| **Get** | `GetAsync(id)` | Fetches one `StoredMemory` by id (including retracted/superseded — the row always survives). |
| **Mark accessed** | `MarkAccessedAsync(ids)` | Stamps `last_accessed_at`. Caller-driven: recall does **not** auto-touch. |
| **Retract** | `RetractAsync(id)` | Soft delete — sets `retracted` / `retracted_at`; the row survives and stops appearing in recall/recollect. Idempotent. |
| **Optimize** | `OptimizeAsync()` | Folds unindexed rows into the vector index and compacts the Lance dataset. |

**Dependencies:** [`DuckDB.NET.Data.Full`](https://www.nuget.org/packages/DuckDB.NET.Data.Full) (bundles
the native DuckDB engine; the Lance extension is installed at runtime via `INSTALL lance; LOAD lance;`),
`Kurrent.Quack` (its `DuckDBConnectionPool` backs the store's pooled connection layer), and
`Microsoft.Extensions.AI.Abstractions` for `IEmbeddingGenerator<string, Embedding<float>>`. The caller
owns the embedder; the store never generates or fakes embeddings itself.

**Connections.** The store uses `Kurrent.Quack`'s `DuckDBConnectionPool` **directly**, via a private
`LanceConnectionPool` subclass whose `Initialize` bootstraps each physical connection with `INSTALL lance;
LOAD lance; SET TimeZone='UTC'; ATTACH IF NOT EXISTS '<dir>' AS kx (TYPE LANCE)`. The store's own
`ExecuteAsync` rents from the pool and layers on the retry-on-conflict, stale-handle recycling and
dispose-time checkpoint (below). The pool hands each operation a `DuckDBAdvancedConnection` whose
`CreateCommand()` is the same ADO.NET surface a raw `DuckDBConnection` exposes — so the store's SQL is
written against ordinary `DbCommand` / `DuckDBParameter` / `DbDataReader`, entirely unchanged by the pooling.

## Why it is how it is

Every rule below was **live-validated against DuckDB v1.5.3 + lance extension `533e0ee`**. The deeper
write-ups live in this repo under `.claude/context/docs/designs/`:
[`duckdb-connector-spec.md`](../../../.claude/context/docs/designs/duckdb-connector-spec.md),
[`duckdb-lancedb-reference.md`](../../../.claude/context/docs/designs/duckdb-lancedb-reference.md),
[`ducklance-validation-report.md`](../../../.claude/context/docs/designs/ducklance-validation-report.md).

- **Engine-file stem ≠ attach alias.** The engine file is `kontext_engine.ddb` (stem
  `kontext_engine`) and it is attached as `kx`. If the stem *equals* the alias, `ATTACH IF NOT EXISTS`
  silently no-ops against the engine's own catalog, so writes land in the `.ddb` instead of the Lance
  dataset — **silent data loss**, empirically reproduced. Keeping the stem distinct from the alias is
  load-bearing, not cosmetic.
- **`ATTACH IF NOT EXISTS`.** `ATTACH` is instance-scoped (per DuckDB connection instance), so every
  pooled connection re-attaches; `IF NOT EXISTS` makes that idempotent.
- **Lowercase column identifiers.** Lance's `DELETE` / `UPDATE` predicate pushdown silently no-ops on
  mixed-case identifiers — a retract that "succeeds" but changes nothing. All columns are lowercase
  (`retracted_at`, `superseded_by`, …) so predicate pushdown actually fires.
- **Tags encode as `"scopevalue"` strings.** A `Tag` is flattened to a single `scope<US>value`
  string (unit separator ``) so the tag column is a plain `VARCHAR[]` and "has this tag" is
  `LABEL_LIST` containment.
- **Tag containment is not pushed down → oversample.** The extension does not push `array_has_any`
  into the vector/hybrid scan, so a tag-filtered recall would lose hits that fall outside the default
  `k`. The candidate pool is oversampled to the full row count (`k = COUNT(*)`) whenever a tag filter
  is present, then filtered and re-limited in SQL. Equality prefilters (like `retracted = false`) *do*
  push down and need no oversampling.
- **Multi-tag filter binds a *list literal cast*, not a parameter list.** The filter is
  `array_has_any(tags, CAST(? AS VARCHAR[]))` with the whole tag list bound as one parameter. Binding
  individual values inside `[?]` (an array literal) breaks — one of the two genuine bugs this
  build-out surfaced (see *Verdict*).
- **L2-normalized embeddings + `metric_type = 'l2'` → `similarity = 1 - d/2`.** Vectors are
  L2-normalized before storage and search, and the index uses the `l2` metric. For unit vectors,
  squared-L2 distance equals `2 - 2·cos`, so `similarity = 1 - distance/2` holds **both** before an
  index exists (brute-force pre-index scan returns *squared*-L2) **and** after (the `IVF_HNSW_PQ` index,
  with `refine_factor`, also returns squared-L2). One formula, correct in every state.
- **Session time zone is pinned to UTC.** The connection initializes with `SET TimeZone='UTC'` so the
  `TIMESTAMPTZ` columns read back the exact UTC instant they were written (the store binds `.UtcDateTime`
  and reads with `DateTimeKind.Utc`); without it, DuckDB.NET applies the machine's local offset and
  timestamps round-trip shifted by that offset.
- **Full-text search is a first-class leg: `INVERTED` (BM25) index on `content`.** Created eagerly in
  `EnsureCreatedAsync` (scalar/FTS indexes work at 0 rows, unlike vector indexes), alongside
  `LABEL_LIST` on `tags` and `BTREE` on `id`. Every recall goes through `lance_hybrid_search`, which
  blends this BM25 keyword leg with the vector leg at `alpha := 0.5` — recall is never vector-only.
- **Vector index is `IVF_HNSW_PQ`, created lazily at ≥256 rows.** The index is an HNSW graph over IVF
  partitions with product-quantized vectors (`USING IVF_HNSW_PQ WITH (metric_type='l2', num_partitions=1,
  num_sub_vectors=N, num_bits=8, hnsw_m=16, hnsw_ef_construction=100)`). PQ training needs at least 256
  rows (the `train = false` path is unimplemented in the extension). Below that threshold there is **no**
  vector index and searches are brute-force — which is *correct*, just not sublinear. The index is created
  on the first retain/optimize once the table crosses 256 rows. Quantized (PQ-family) search always passes
  `refine_factor := 4` so small-k ranking stays exact despite quantization error.
- **Soft retraction.** Retract sets `retracted` / `retracted_at`; the row is never deleted, per the
  `StoredMemory` contract (`RetractedAt` is part of the persisted shape). Recall and recollect filter
  `retracted = false`; `GetAsync` still returns the row.
- **Two validated transient-failure shapes are handled** (in the store's `ExecuteAsync` over the pool).
  (1) `"Retryable commit conflict"` — retried on the **same** connection with a short backoff (×3). (2) A
  **stale dataset handle** (`LanceError(IO) … Not found`, or `belongs to non-existent fragment`) — the
  connection is disposed and its pool `Scope` is **dropped un-disposed** (Scope.Dispose is the pool's only
  liveness-unchecked re-admit path, so dropping it keeps the poisoned connection out), then the operation
  is retried **once** on a fresh connection. The bootstrap `Initialize` likewise disposes the connection if
  it throws, because Kurrent.Quack's `Open()` does not.
- **Stable engine file, checkpointed on dispose.** The engine file is `kontext_engine.ddb` (stem
  `kontext_engine`, distinct from the `kx` alias). It is `CHECKPOINT`ed on dispose (best-effort; it
  replays from the WAL otherwise) and is **never deleted**.
- **Single-process ownership.** DuckDB holds a single-process lock on its engine file, so exactly one
  `KontextStore` instance owns a given storage path per process. (The Lance directory itself is
  multi-reader via other tooling — this constraint is only about the DuckDB engine file.)

## Provenance and relationship to DuckLance

This prototype did not appear from nothing — it is the end product of a longer effort that was built
and battle-tested **elsewhere**:

- **DuckLance**, a full `Microsoft.Extensions.VectorData` connector over the same DuckDB+Lance stack,
  was built first, in Sérgio's fork of microsoft/semantic-kernel:
  `~/dev/contrib/semantic-kernel/dotnet/src/VectorData/DuckLance/` (+ `DuckLance.Tests`, 449 tests,
  and a `PORTING.md` making the folder copy-paste portable). **It is right there to learn from or
  port** — every rule this README states was proven there first, usually the hard way (adversarial
  reviews, live probes, stress runs).
- The **design docs, validation report and rerunnable spike harness** live in THIS repo:
  `.claude/context/docs/designs/duckdb-connector-spec.md`, `duckdb-lancedb-reference.md`,
  `ducklance-validation-report.md`, and `lancespike/` — the 56-check harness whose outputs are the
  golden oracles both codebases assert against.

This store is the **abstraction-free distillation** of that connector. It carries
the *same* live-validated DuckDB+Lance knowledge with far less machinery, because the record type is
fixed (the MCP `Memory` / `StoredMemory` shape) instead of being generic over arbitrary
`Microsoft.Extensions.VectorData` records. Even the connection layer stays abstraction-free: rather than
wrap the pool in a separate manager class, the store uses `Kurrent.Quack`'s `DuckDBConnectionPool`
**directly** via a private `LanceConnectionPool` subclass, carrying DuckLance's identical validated
disciplines inline — the `lance`/UTC/ATTACH bootstrap, an `Initialize` that disposes on failure,
retry-on-conflict, and the drop-the-Scope stale-handle recycle.

- Need **MEVD compatibility** (a `VectorStore` a host can swap connectors under, arbitrary record
  types, the full filter surface)? Use **DuckLance**.
- The app just needs **retain / recall / recollect / retract** over the Kontext contracts? Use
  **KontextStore** — one file, no abstraction tax.

## Verdict

Fully doable in one file. The live demo exercised every operation successfully, including
evidence JSON round-trip, supersedes chains, and reopen-durability (dispose → reopen → data and search
intact). Building against the **real** MCP contracts (rather than an invented record shape), and
asserting every field in tests, is what surfaced three genuine bugs the prototype then fixed:

1. **Multi-tag array binding** — the correct shape is `array_has_any(tags, CAST(? AS VARCHAR[]))` with
   the list bound as a single parameter; binding values inside an array literal `[?]` fails.
2. **Pre-index similarity formula** — brute-force (pre-index) search returns *squared*-L2, so the
   similarity conversion is `1 - d/2` (the same as post-index), not a cosine-from-raw-distance guess.
3. **`TIMESTAMPTZ` local-offset round-trip** — asserting exact timestamps (which the demo never did)
   showed values coming back shifted by the machine's local UTC offset; pinning the session to
   `SET TimeZone='UTC'` makes the write and read symmetric so instants round-trip exactly.

## What's missing (honest gaps)

- **No batch retain.** `RetainAsync` takes one `Memory`; the MCP `RetainResult` implies a batch. Batch
  retain (one embedding call, one commit for many memories) is not implemented yet.
- **`MinScore` filters post-fetch**, so a page can return **fewer than `Limit`** results even when more
  matching-but-lower-scoring memories exist.
- **No scope-wildcard tag queries.** "All tags in a scope" (e.g. every `work:*`) would need a second
  array column of bare scopes; only exact `scope:value` containment is supported today.
- **No background reindex scheduler.** Freshness relies on the lazy ≥256-row index plus manual
  `OptimizeAsync`. DuckLance has a query-driven reindex scheduler; port it here if index freshness
  under steady writes matters.
- **No automated VACUUM policy.** Compaction happens only in `OptimizeAsync`. **Caution:** a native
  Lance panic was observed once in the cleanup path under *concurrent* vacuum — keep vacuum manual and
  rare until that is understood.
- **`LastAccessedAt` is caller-driven** via `MarkAccessedAsync`. Recall deliberately does not auto-touch
  accessed rows (the caller decides when a recall counts as an access).
- **Test coverage is a fraction of DuckLance's** 449-test suite (see `VectorStore/` tests in
  `Kurrent.Kontext.Tests`).
- **Single-process ownership is by design** (the DuckDB engine-file lock). The Lance dataset directory
  itself remains multi-reader via other tooling.

## Namespace note

The folder is `VectorStore/`, but the namespace is deliberately **`Kurrent.Kontext.Storage`** — a
`Kurrent.Kontext.VectorStore` namespace would shadow the `Microsoft.Extensions.VectorData.VectorStore`
*type* that `KontextMemory` and `TestVectorStore` reference (`KontextMemory(VectorStore store, …)`
becomes CS0118 the moment the namespace exists). If a different non-clashing name reads better
(`Kurrent.Kontext.Memory.Store`, `Kurrent.Kontext.Lance`, …), renaming is a find-and-replace — just
never name the namespace after the MEVD type.

## Open decisions

1. **Test embedding generator.** The tests currently use the real SK ONNX Bert generator
   (`Microsoft.SemanticKernel.Connectors.Onnx`, one additive CPM pin at `1.78.0-alpha`, model cached at
   `~/.cache/ducklance-tests/bge-micro-v2/`, skip-if-absent). Alternative with no props pin and no
   download: the in-repo `WordPieceOnnxEmbeddingGenerator` from `Kurrent.Kontext.Embeddings` (embedded
   model, needs a project reference). Either satisfies the "no fake generators" rule.
2. **Which store does `KontextMemory` ship on?** This prototype (no abstraction, one file) or the
   DuckLance MEVD connector (swap-under-a-host `VectorStore`, packed from the SK fork to a local
   feed). Both are valid; this is the architecture call that decides the integration work.
3. **Batch retain priority.** The MCP `RetainResult` shape implies batch retain; implementing it is
   mostly mechanical (one embedding batch + one multi-row MERGE with last-wins in-batch dedup — the
   validated shape exists in DuckLance's composer).

## Handoff to whoever continues this

State when this was written (2026-07-18): everything here is **working-tree only, uncommitted**, tests
32/32 green (21 pre-existing + 11 new). The sibling DuckLance connector sits in the semantic-kernel
fork at 449/449, also uncommitted.

**Trust order when something contradicts something else:** live behavior of the extension > the
validation report / spike harness (`.claude/context/docs/designs/`, `lancespike/` — rerunnable) > this
README > the spec prose. Every rule in *Why it is how it is* was learned by watching a "reasonable"
assumption fail against the real extension — when in doubt, spike it before trusting any doc,
including this one.

**Do not "clean up" these — each guards a validated failure mode:** the engine-file stem ≠ attach
alias; lowercase column names; `ATTACH IF NOT EXISTS`; the un-disposed pool `Scope` on the
stale-handle path (disposing it re-admits a dead connection — Quack's `TryReturn` has no liveness
check); disposing the connection inside `Initialize`'s catch (Quack's `Open()` leaks it otherwise);
L2-normalization + `1 - d/2` (raw `1 - d` is wrong before the index exists); `SET TimeZone='UTC'`;
oversampled `k` for tag filters; real embedding generators in tests, never fakes.

**Most useful next steps, in rough order:** resolve the three open decisions above → batch retain →
wire `KontextMemory` onto the chosen store and run its integration tests → port DuckLance's reindex
scheduler if index freshness under steady writes matters → report the upstream bugs (lance-duckdb:
mixed-case DELETE/UPDATE pushdown, BLOB-as-stream reads, "non-existent fragment" errors, a native
panic in the vacuum/cleanup path under concurrency; kurrent-quack: the `Open()` leak on `Initialize`
failure).

The full build history — every probe, adversarial review, and dead end — lives in the session records;
the condensed durable facts are in the project memory files alongside this repo's design docs. Good
luck, and validate before you trust. 🦆
