---
title: Kontext Records Indexer
status: settling         # exploring | settling | superseded
authors: [sergio, claude]
date: 2026-08-10
tags: [kontext, lance, duckdb, quack, indexing, search]
---

# Design Space — Kontext Records Indexer

<!--
Working doc. Brainstorm, discussion, and decisions for this feature. Deliberately informal and
append-leaning — you add to it, you mark decisions, you do not rewrite the history of the discussion.
Kept for the life of the feature. Once it settles, distill the outcome into prd/prd.md and spec/spec.md,
and slice releases into plans/. This doc is also the feature's decision record — keep the rejected
options; the "why not" is the value. Sources this design space cites go in design/refs/.
-->

## Problem / Trigger

Kontext needs whole-log search: consume the entire KurrentDB `$all` log and index it for
full-text (BM25) and vector search in a Lance table, queried through DuckDB. Modeled on
`KurrentDB.SecondaryIndexing`'s default index pipeline (`DefaultIndexSubscription` →
`DefaultIndexProcessor` → DuckDB), which was mapped in full during the 2026-08-10 design
session. The Quack appender gained LIST/ARRAY support (`#59`, shipped `0.0.0-alpha.217+`),
which unblocks writing `FLOAT[N]` embedding columns at appender speed.

## Exploration

Reference pipeline facts that shaped the design (all verified against source this session):

- `idx_all` stores positions, not payload; reads resolve events from the log via
  `get_kdb_def(log_position)`. The table itself is the checkpoint (last row by `rowid`).
- The `$`-prefix system-event filter lives in the subscription, not the processor.
- One Quack appender `Flush()` = exactly one Lance commit, any row count; each SQL
  `INSERT` is its own commit (`AppenderLanceProbeTests`).
- `duckdb_appender_create` has no catalog slot — the only route into the Lance catalog is
  `USE ldb` session redirection.
- Reference defects deliberately not copied: poison event kills the subscription
  permanently and silently; `deleted` column hardcoded `false`; bus-path shutdown disposes
  processor before subscription.

Rejected along the way (see Decisions for winners):

- Staging table + `INSERT…SELECT` with VARCHAR→`FLOAT[N]` cast — designed to route around
  the appender array gap; obsolete the moment the gap was confirmed closed in the pinned
  package (alpha.221 contains `AddCollection`/`CollectionType`).
- `UBIGINT` for `log_position` — the value is `Int64` end to end (`TFPos`, `idx_all`);
  unsigned buys only casts. Consequence: `memories.log_position UBIGINT` is now wrong and
  gets fixed to `BIGINT`.
- `commit_position`, `stream_hash`, `deleted`, `expires_at` columns — read-path
  reconstruction concerns of `idx_all` that a search surface does not have.
- Table names `log_index` and `events` — everything in the Lance store is an index, and
  the table holds no event bodies.
- Per-column metadata-only rows for non-decodable payloads — collapsed once the
  `ContentExtractor` null-return contract made skip the natural policy.
- Deferred embedding backfill lane — impossible by construction once `embedding` went
  NOT NULL.
- BufferedView zero-copy uncommitted-read machinery — secondary-indexing read-path
  concern; Kontext reads via the lance TVFs and tolerates a one-batch freshness gap.

## Decisions

- 2026-08-10 — Table is `ldb.main.records` (own dataset `<storage>/records.lance`), same
  `ldb` attach as memories; per-table datasets isolate commits, so no contention with the
  memories projector.
- 2026-08-10 — Schema: `log_position BIGINT NOT NULL`, `record_id BLOB NOT NULL`,
  `stream VARCHAR NOT NULL`, `category VARCHAR NOT NULL`, `schema_name VARCHAR NOT NULL`,
  `schema_id VARCHAR NULL`, `schema_format VARCHAR NOT NULL`, `content VARCHAR NOT NULL`,
  `embedding FLOAT[Dimension] NOT NULL`, `created_at BIGINT NOT NULL` (epoch ms).
  Scalar columns are the prefilter surface — scalar equality pushes down into the lance
  scan; array containment does not (the tags lesson).
- 2026-08-10 — `created_at` kept: recency is one of the three retrieval components in the
  Generative Agents model this system is grounded in, and retrofitting the column later
  is a full backfill.
- 2026-08-10 — Indexes: `content_fts` INVERTED on `content`, `vec_idx` IVF_HNSW_PQ on
  `embedding` (memories params, 256-row training floor pattern), BTREE on `log_position`.
- 2026-08-10 — Content extraction is a `ContentExtractor` delegate:
  `string? Extract(in ResolvedEvent record)`, `null = skip` (no row written). The delegate
  is the single authority over what gets indexed. Default v1: `schema_format == Json` →
  complete payload as string, else null. Flattened `key: value` extraction and
  schema-registry-aware extraction are future delegate swaps, not redesigns.
- 2026-08-10 — Embedding input ≡ extractor output, nothing synthesized. Generator is a
  ctor-injected `IEmbeddingGenerator<string, Embedding<float>>` (memories precedent);
  dimension agreement with the table is owned by the DI wiring. Embedding is inline by
  construction (`NOT NULL` forbids a backfill lane).
- 2026-08-10 — Write path: extract → batch → embed → Quack `Appender` under `USE ldb` →
  one `Flush()` per batch = one atomic Lance commit. Checkpoint is the table:
  `max(log_position)`; atomic batch commits mean resume can neither duplicate nor lose.
- 2026-08-10 — Subscription mirrors `DefaultIndexSubscription`: `Enumerator.AllSubscription`,
  `requiresLeader: false`, `$`-prefix filter replicated (it lives in the subscription).
  Node scope: every node indexes its local log into its local `ldb` — the secondary-index
  pattern verbatim; the log is the replicated thing, the index is a local derivation.
- 2026-08-10 — Hosting: `Kurrent.Kontext`, a `SystemReadyBackgroundService` beside
  `KontextProjectorService`, same DI wiring.
- 2026-08-10 — Supervision inverts the reference: backoff restart on subscription death;
  poison record = retry once, then skip with error log + counter. A search index tolerates
  a missing row; a stalled index does not.
- 2026-08-10 — Tombstones: no column, gap inherited knowingly. Future mitigation exists
  structurally: hits hydrate from the log, so a scavenged event fails hydration —
  drop the hit, lazily `DELETE FROM records` on miss.
- 2026-08-10 — Known accepted v1 truncations: embedder truncates long input at its own
  window; `content` column is uncapped (pathological multi-MB payloads bloat row + FTS).
  Both are delegate-fixable later.
- 2026-08-10 — Corrective prerequisite: `memories.log_position UBIGINT → BIGINT`
  (`KontextSchema` DDL + binding sites), own commit. `CREATE TABLE IF NOT EXISTS` will not
  retype an existing dataset — deployed tables need rebuild (re-embed path is documented).

- 2026-08-10 (implementation) — The checkpoint reads on the writer's own freshly-opened
  connection, never a rented pooled one: a pooled connection can hold a stale lance dataset
  handle and report an older table version, and a stale checkpoint replays into duplicate
  rows (the appender inserts blindly). Found live: a rented connection that had scanned the
  empty table kept reporting it empty after another connection's flush.
  `KontextRecordsSchema.ReadLastPosition(connection)` takes the connection for this reason.
- 2026-08-10 (implementation) — `ContentExtractor` gained a second parameter,
  `string schemaFormat`, resolved once by the writer from the record's properties
  (falling back to the IsJson flag) so extractors never re-parse the protobuf Struct
  per event at whole-log rates.
- 2026-08-10 (implementation) — Poison policy split by failure class: a deterministic
  extractor throw skips the record with a warning + counter (`SkippedRecords`); an
  embed/append/flush failure propagates so nothing commits and supervision replays the
  batch from the checkpoint — no data loss either way.

- 2026-08-10 (review cycle, Sérgio) — THE RESHAPE, superseding several v1 decisions above:
  - The Surge `SystemConsumer` replaces `Enumerator.AllSubscription`. Source-verified: the
    default filter is `ExcludeSystemEvents()` applied server-side; `SkipDecoding()` skips
    deserialization (raw `Data` + resolved `SchemaInfo`, `Value` null); `Headers.Decode`
    swallows arbitrary metadata, so the consumer is whole-log-safe; `CheckpointReceived`
    control records advance `batch[^1].Position` through skipped stretches, fixing
    table-as-checkpoint's restart re-scan defect. `DisableAutoCommit` mandatory (the
    consumer's own store writes to a replicated stream — cross-node poison for node-local
    indexes). Filter lives in `KontextConventions.Filters.RecordsIndexFilter`.
  - `BufferedAppender` replaces the raw `Appender` (chunk writes vs per-value FFI at
    whole-log rates; `UserIndexProcessor` precedent). The earlier "raw Appender is the
    candidate primitive" note conflated `BufferedView` (read machinery) with
    `BufferedAppender` (write-side buffering). Probe-gated and passed: chunk-append reaches
    lance via USE, `FLOAT[N]` ARRAY round-trips, one flush = one commit.
  - Checkpoint moves into a LANCE-resident `KontextCheckpointStore` table sharing the batch
    transaction. `TransactionLanceProbeTests` pinned: a transaction writing lance cannot
    touch another attached database (engine refuses); within lance, rollback reverts writes
    across tables INCLUDING an appender flush; commits are one lance commit per table per
    transaction. This kills table-as-checkpoint, the `Max(store, lanceMax)` dual read, and
    the high-water guard — resume is a plain `Load`. The 2026-08-03 "per-statement commits,
    no tx atomicity" finding is overturned on the current engine build. Residual window,
    accepted: a crash inside duck's commit between two datasets' native commits.
  - Lance `CREATE TABLE` rejects constraints, so `KontextCheckpointStore` went
    constraint-free with a facet-guarded MERGE upsert (monotonic guard in the MATCHED arm) —
    one class, catalog decided by the connection.
  - Hosting and the loop split: thin `KontextRecordsIndexerService` shell over
    `KontextRecordsIndexer` (loop + supervision). Connection configuration moved into the
    infrastructure: `KontextConnectionPool.OpenLanceWriter()` returns the dedicated,
    lance-redirected writer connection.
  - `ContentExtractor` renamed `RecordContentExtractor`, now `string? (SurgeRecord)` —
    the schemaFormat parameter and the writer's protobuf `Struct` parse both died because
    `SchemaInfo` arrives resolved; `schema_id` fills from `HeaderKeys.SchemaId` when present.

- 2026-08-10 (final challenge, Sérgio) — "why not just resume from the last indexed row's
  position?" examined to the bottom and RULED: **the checkpoint store stays.**
  `max(log_position)` alone is correct (no duplicates possible, no transaction needed, the
  two-dataset commit window disappears) but its resume point only advances on indexed rows —
  every restart re-reads the trailing run of skipped records, unbounded on a JSON-poor log.
  The store caps restart cost at O(1) regardless of log shape, at the price of the batch
  transaction and the (accepted) two-dataset commit window. Same split the secondary indexes
  ship: dense idx_all resumes from its own last row; the sparse user index carries a
  checkpoint table.

- 2026-08-11 (bootstrap config, Sérgio) — **`KontextOptions` is the ONE owner of the
  `KurrentDB:Kontext` section.** The prototype's file contract survives (Provider + one block
  per provider, only the active block written); `Dimension` (default 384, probe-validated via
  `EnsureDimensionAsync`) is the one added key. The section collision died: `OnnxModelRegistry`
  no longer binds `Kontext:Embeddings` — its keys (`ModelsDirectory`, `ModelId`, `Models[]`)
  moved INSIDE the Local provider block, and the registry is built from those options.
  Boundary ruling: the config aggregate is Kontext's (`KontextOptions` +
  `KontextEmbeddingsOptions` in Kurrent.Kontext); the embeddings library owns the
  `EmbeddingsProvider` enum and the `AddKontextEmbeddings(provider, …typed blocks…)` switch —
  it takes pieces, never the host's config class. Local ladder: no registry configured →
  shipped interim pmm12; registry configured → SentencePiece engine from disk-cached models —
  which is what eventually deletes every embedded model bundle (the csproj flag from this
  morning is a stepping stone). Rejected along the way: moving the config aggregate into the
  embeddings library ("it's a Kontext config that happens to contain embeddings config").

- 2026-08-11 (bootstrap executed, Sérgio's rulings) — **the plugin is two lines**:
  `AddNodeSystemInfoProvider().AddKontext(configuration)` and `UseKontextMcp().UseKontextGrpc()`.
  `AddKontext` is a chain of decomposed groups (Options/Storage/Embeddings/Retrieval/Memory/
  GrpcEdge/McpEdge/Indexing), each independently callable, each self-contained (the gRPC edge
  registers `AddGrpc` itself; the MCP edge owns `WithHttpTransport`). **No engine file** —
  everything durable lives in lance, so the pool runs `Data Source=:memory:` per connection
  (pinned by `InMemoryEngineProbeTests`: writer tx shape + rented readers with private
  in-memory catalogs sharing only the ATTACH). `KontextBootstrapService` is the startup gate:
  dimension probe + memories schema, hosted first. The Surge registrations come from the
  system, not the plugin. The `Action<KontextRetrieverBuilder, IServiceProvider>` hook is GONE
  everywhere — pre-registration of a builder-composed retriever is the variant seam
  (first-wins), and the retrieval registration tests were removed by ruling. Workspaces are
  dead; the MCP gate is authenticated-user until a Kontext-owned operation lands.
  `AddKontextEmbeddings` naming: parked, revisit at the end.

- 2026-08-11 (hosting shakedown) — three corrections from actually booting the system in a node:
  - `KontextBootstrapService` DELETED (my unrequested invention): the solved primitive is
    Core's readiness-gated `AddSystemStartupTask` (`SystemStartupManager`), the same mechanism
    SchemaRegistry and Connectors use — "Kontext Bootstrap" runs the dimension probe + memories
    schema there. Message registration follows the same house pattern: "Kontext Message
    Registration" startup task registering all five contract events via
    `KontextConventions.RegisterMessages<T>` (copied from SchemaRegistryConventions minus its
    Eventuous mapping — deduplicate into Core later, per ruling).
  - ROOT CAUSE of the node-readiness deaths, found by worktree bisection + ctor instrumentation:
    **Core's `SystemReadiness` has two public constructors and MEDI cannot disambiguate —
    `TryAddSingleton<SystemReadiness>()` threw at first resolution, faulting host startup
    silently (reads as a 10s readiness timeout).** Latent since the class was written; exposed
    the first time anything hosted a `SystemReadyBackgroundService` in a node. Fixed twice:
    first a factory registration (symptom, one site), then — Sérgio's ruling — the
    `(IServiceProvider)` convenience constructor was DELETED so the class has one constructor
    and plain `TryAddSingleton` composes it; the factory and its comment died with it. License
    and routing order were investigated and acquitted; `UseRouting` before `UseEndpoints` kept
    anyway (SchemaRegistry precedent).
  - Plugin is enabled by default (Connectors-style cascade, Sérgio's edit) and the full system
    now boots green inside every test node: 42/42.

- 2026-08-11 (storage-layer cleanup, Sérgio's rulings) — three commits, in order:
  - `81bf8e026` — the pool is in-memory only. Ctor is `(storagePath)`; `Data Source=:memory:`
    is a hardcoded const (pinned by `InMemoryEngineProbeTests`) and the alias is hardcoded
    `ldb`. The dispose-time CHECKPOINT, `_everExecuted`, and the stem-collision half of
    `VerifyLanceNamespace` die (the in-memory engine catalog is always `memory`); the
    is-attached check stays. Every test `NewPool` helper is `new(dir)`.
  - `f29a09b79` — the memories projector gets the records-indexer supervision, shape verbatim:
    `KontextMemoryProjector` extracted (loop + connection + supervision, 5s→60s backoff,
    restart re-opens the connection and resumes from the checkpoint);
    `KontextMemoryProjectorService` is a thin hosting shell like `KontextRecordsIndexerService`.
    Before this, a lance commit conflict killed the hosted service silently.
  - `1ca55339d` — ONE acquisition surface. `KontextConnectionPool` composes a private Quack
    pool instead of being one: public surface is `ExecuteAsync` (ReadOnly) and
    `OpenLanceWriter` (Writer); `Open`/`Rent` are internal, test-only via InternalsVisibleTo —
    the scoped-handle machinery is off the consumer surface. The name stays
    `KontextConnectionPool` (ruling). Deleted with the duality, by ruling: the commented-out
    `KontextConnectionProvider` and the dead Surge projection subgraph (`KontextProjection`,
    `KontextMemoryProjection`, `KontextProjectorService<T>` — zero callers, resolved an
    `IDuckDBConnectionProvider` nothing registers — and its tests, the one consumer that
    needed the pool's inheritance). `MemorySeeding.Insert` now opens one lance writer for the
    whole corpus — seeding is writing, and writers never rent.

## Open Questions

- Batch size / time-box defaults (500 / 5s, memories precedent) and the 30s vector-index
  optimize throttle — tune with observed numbers.
- Records dataset compaction/vacuum: not yet wired into a maintenance scheduler;
  manifest count grows one per batch until it is.
- `KontextRecordsSchema` duplicates `KontextSchema`'s index-lifecycle mechanics
  (training-floor try-catch, SHOW INDEXES, DDL runner) — merge candidate, deliberately
  not restructured mid-feature.
- No end-to-end service test (subscription against a live node) — writer, schema, and
  appender capability are covered; the subscription loop follows `DefaultIndexSubscription`
  verbatim. Needs the in-server harness when hosting lands.
- Pre-existing failures observed while landing this (not records-related):
  `EvidenceStructBindingProbeTests` pins an engine limitation the vendored lance build has
  fixed (pin update is a design decision — it justifies the evidence `VARCHAR[]` shape),
  and the two ranking benchmarks flap around their floors run to run.
- ~~LATENT DEFECT, memories side~~ FIXED 2026-08-10 (`b899575`): the memories projector now
  opens `pool.OpenLanceWriter()`, so its checkpoint lands in the lance catalog and the batch
  transaction touches one catalog; pinned by
  `KontextMemoryWriterTests.batch_and_checkpoint_commit_and_revert_together`.
- ~~The provider simplification (one facade ensuring schema, returning ReadOnly | Writer
  connections; scoped-handle machinery off the consumer surface) — endorsed, not yet
  executed beyond `OpenLanceWriter()`.~~ EXECUTED 2026-08-11 (`1ca55339d`) — see the
  storage-layer cleanup decision below.
