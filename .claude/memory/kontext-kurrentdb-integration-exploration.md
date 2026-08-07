---
name: kontext-kurrentdb-integration-exploration
description: Kontext↔KurrentDB integration — projector/stream-name DECIDED 2026-07-21 (Surge DuckDBProjection, $kontext/memories); hosting/append mechanism still open
metadata: 
  node_type: memory
  type: project
  originSessionId: 5efcc2a3-5207-42e4-8b2d-4438c5af3a19
  modified: 2026-08-05T14:44:09.190Z
---

As of 2026-07-18, Sérgio is explicitly in exploration mode on the Kontext↔KurrentDB
integration — "going back and forth until I finally find balance." **No decisions have been
made.** Do not treat any of the following as settled; they are options floated and analysis
rendered.

Options on the table:

- **Store choice** — the central tension: full DuckLance MEVD connector (copied into repo at
  `src/Kurrent.Kontext.DuckLance/` + `.Tests/` for review/reformatting: ~5.6k source lines /
  28 files + ~12.4k test lines / 45 files) vs one-file `KontextStore`
  (`src/Kurrent.Kontext/VectorStore/`, ~570 + ~500 lines). Sérgio's stated concern is the
  responsibility and code volume the complete MEVD implementation brings.
- **Append mechanism** — in-server via Surge `SystemProducer` was floated; single append
  stream was floated. Table-function-as-append was assessed as a bad fit (retry replay, no
  cross-system atomicity, sync-over-async); a read-side `kurrent_stream(...)` ingest function
  remains a candidate (`RegisterTableFunction` verified in DuckDB.NET 1.5.3).
- **Write-through idea — PARKED 2026-07-20, append-only chosen for now**: Sérgio ruled retain and
  recall APPEND events (e.g. MemoriesRecalled) and the reactor is the SOLE read-model writer;
  write-through (inline update after append, idempotent projector as backstop) stays a future
  latency optimization, explicitly not built now. Claude's soundness conditions if it is ever
  adopted: append FIRST (response derives from the append); `log_position` column with strict `>`
  apply guard on BOTH writers; one shared fold in the data store (service and reactor call the
  same apply verb); content-only events (rebuild re-embeds = embedding-model migration path).
  Guarded-UPDATE pushdown (`WHERE id = ? AND log_position < ?`) through the lance extension is
  unverified. Touches would stay reactor-only even under write-through (staleness harmless — the
  TouchBuffer is a stopgap that dissolves into the reactor once events land).
- **Separation of concerns — ADOPTED 2026-07-19 as `KontextDataStore`**: Sérgio's ruling — the
  data access layer never leaks. `KontextDataStore(VectorStore, TouchBufferOptions?)` in
  `Kurrent.Kontext.Data` owns ALL persistence (memories collection now; entities, directives and
  more tables coming — it creates the schema) with a domain-verb, Contracts-typed surface
  (EnsureSchema/Get/Save/Search/List/FindDerived/MarkAccessed); MemoryRecord + mapper + filters +
  TouchBuffer are its internals. It is the ONE place allowed to later query DuckDB directly
  (partial-update stamps, hybrid tuning) — DuckLance must expose a public door first
  (ConnectionManager is internal). `KontextMemory(KontextDataStore)` keeps only workflows and the
  future non-storage concerns (KurrentDB appends, cross-encoder/reranking retrieval). The old
  hand-written-SQL `Data/KontextStore.cs` is intentionally KEPT for comparison, not wired.
- **Core model** ([[kontext-reloaded-canonical-model]]) — Contracts vs Mcp.Model in the store:
  open. One layering observation stands regardless: `KontextStore` importing types from
  `Edges/Mcp/Model` makes storage depend on an edge.
- **DuckDB-native body table** (floated 2026-07-19, Sérgio's idea): keep Lance minimal (search
  surface only) and store the memory BODY (Evidence blob, supersedes chain, validity, …) in native
  DuckDB tables in the already-present `.ddb` — same engine, so full recall becomes ONE statement
  (`lance_hybrid_search JOIN duck.main.memory_bodies`), and the Lance index becomes rebuildable via
  `INSERT..SELECT` (no re-embedding if the body table also copies the vector). Makes standalone mode
  much stronger; composes with stream-first (log=truth, duckdb=body cache, lance=index). TWO SPIKES
  before trusting: (1) cross-catalog JOIN against the search table functions, (2) the recalled-but-
  unverified rule that one transaction can write only one attached catalog (→ body-first write order
  + reconcile repair).
  REFINED same night: lance holds ONLY the hybrid-search minimum (memory_id, content/INVERTED,
  tags/LABEL_LIST, vec; memory_type only if recall filters it) — retracted memories are DELETED from
  lance (truth keeps the soft-delete in duck); recollect + retract cascade + sort keys move to duck
  entirely (native listing). Lance becomes write-once/delete-on-retract — no row rewrites, ideal for
  the fragment model. Open: whether superseded rows also delete from lance (recall-semantics call).
- **RecordCodec (2026-07-19): multi-vector IS supported via NAMED slots** — Sérgio REVERSED the
  earlier single-vector-only ruling after learning the mapper already supports multi-vector
  end-to-end (`DuckDBMultiVectorTests`); do not resurrect the single-only scope. Final agreed API:
  `VectorText(Column, Text)` + `VectorSlots` (name-addressed, pipeline-constructed) +
  `RecordCodec<TRecord>` root (plural, `VectorSlots[]` batch — NO jagged arrays on any surface) +
  `SingleVectorRecordCodec<TRecord>` convenience tier (sealed routing; the 99% case, three sync
  members). Resolution: registered codec wins, `DuckDBModelCodec` unconditional default.
  **LANDED 2026-07-19 (all phases)**: named-slots reshape, `DuckDBVectorStoreOptions.Codecs`
  registry, collection swapped to the codec (5 read sites → `Decode`, upsert →
  `VectorizeBatchAsync` + `Encode`, MEVD per-property dispatchers preserved), mapper + its tests
  DELETED (scalar-matrix cases ported), full suite 454/454 incl. the engine-level parity tests.
  One API revision during review: `ToWireVector` demoted from public to local functions.
  Design record: `.claude/context/docs/designs/2026-07-19-record-codec/design.md`.
- **KontextDataStoreV2 (2026-07-20, iterating)**: the read-model accessor over a BORROWED
  `DuckDBConnectionPool` — no vector store anywhere; const raw-string SQL inside the methods that
  run them. RULING 2026-07-20: every value binds as a named `$parameter` (SchemaQueries style),
  NEVER interpolated into the text — Lance knob named-args bind too (live-probe validated; the
  earlier "cannot bind" belief was wrong). Only clauses (optional knobs) and the FLOAT[N]
  dimension are interpolated; list parameters for variable arity. ListAsync is one true const:
  sort direction is the $direction sign-flip (±1 on DOUBLE-coerced keys, no ASC/DESC pick),
  importance-primary tie-breaks by last_accessed_at inheriting the direction, memory_id closes
  every sort for a total, deterministic order. Schema is `underscore_lowercase` (memory_id, retained_at, …), OWNED BY THE PROJECTOR —
  DuckLance's model-builder naming is explicitly a non-concern for this path ("we do not care").
  Search LANDED (2026-07-20) as three `SearchAsync` overloads dispatched by required inputs —
  vector (`lance_vector_search`, `_distance ASC`), full-text (`lance_fts`, `_score DESC`), hybrid
  (`lance_hybrid_search`, `_hybrid_score DESC`) — with per-mode options classes
  (`VectorSearchOptions`/`FullTextSearchOptions`/`HybridSearchOptions`, flat, knobs duplicated on
  purpose) and all-nullable scores on `MemoryHit` (a mode's unproduced score is null, never
  fabricated). Tag containment is never pushed down: all three modes oversample k to the table
  count. GetLineageAsync (2026-07-20): the supersession family is a FOREST (single
  superseded_by column = one successor max = no diamonds); ONE recursive CTE (WITH RECURSIVE +
  set-based UNION over lance scans — live-validated) walks the superseded_by column in BOTH
  directions with plain equality; the supersedes arrays are never consulted, chronological out.
  Sérgio explicitly overrode the initial iterative-walk version — prefer the CTE. Flat
  StoredMemory list, no ids-only variant (nodes carry their own edges). OPEN INVARIANT for the
  write path:
  KontextMemory must reject superseding an already-superseded memory, or history is silently
  overwritten. V1 `KontextDataStore` + old `KontextStore` deliberately kept for comparison.
  FUTURE (not now, Sérgio's ruling): DuckLance's model builder should also emit
  underscore_lowercase storage names.
  `KontextConnectionPool` (2026-07-20) owns the engine: Initialize = lance + ATTACH + engine-side
  verification (alias == current_database() detects the stem-collision no-op — the TYPE column
  reads 'duckdb' even for lance attaches, validated), checkpoint-on-dispose, frozen-pool guard.
  RULING: renting (`ExecuteAsync`, Polly `ResiliencePipeline` stale-handle recycle — DELIBERATE;
  a 2026-07-20 session replaced it with a manual loop on a misunderstanding and Sérgio reverted
  that same day, do not remove Polly again) is the READ surface only — WRITERS
  never rent; the projector holds a dedicated `Open()` connection (prepared-statement reuse,
  transactions) and carries the Lance commit-conflict retry around its own commits.
- **Scope-isolation direction (2026-07-20, NOT yet decided which)**: promote 1–2 scope identities
  (tenant_id first candidate; session/project/user maybe) from tags to scalar columns — ANDed
  equality pushes down (EXPLAIN-observed, "Lance Pushed Filter Parts"), giving true prefilter
  isolation; array containment can NEVER push down in this extension (missing from the wire IR
  both sides; `filter :=` escape hatch is enforced REST-only — source-confirmed at HEAD, clone
  `~/dev/contrib/lance-duckdb`, installed build 533e0ee probed live). Until Sérgio picks the
  columns, CHANGE NOTHING. Decorative tags stay: List filtering + opt-in exact search gate
  (oversample); topical steering later moves to pipeline ranking (RRF/boost — Park-style blend).
  Fallback if oversample ever hurts: delimited tags_text + contains() pushes down (observed) but
  is stringly/unindexable — not for isolation.
- **DECIDED 2026-07-21: the read-model projector uses the Surge prototype's
  `DuckDBProjection`/`DuckDBProjector` exactly like SchemaRegistry's `SchemaProjections`** —
  Sérgio's explicit ruling ("we use the DuckDBProjection JUST LIKE SchemaProjections"; the old
  prototype Surge is fine because it ships in kurrentdb today). Landed:
  `Modules/Memory/Data/KontextMemoryProjection` (MemoryRetained/MemoryRetracted/MemoriesRecalled;
  ReflectionCompleted deferred — scalar superseded_by vs parallel event arrays is an open contract
  question; MemoriesAccessed/Reclaimed unhandled), `KontextMemoryProjectorService` (plain runner;
  the NodeBackgroundService/SystemReady + IHostedService wrapper belongs to whatever host embeds
  Kontext — WHERE Kontext hosts is still open), `KontextConventions` with stream prefix
  **`$kontext/memories`** (decided), `log_position UBIGINT` per-row stamp (decided, named by
  Sérgio). Provider seam SOLVED: Surge's `DuckDBAdvancedConnectionProvider(pool)` just delegates
  to Quack's `DuckDBConnectionPool`, so it wraps `KontextConnectionPool` directly and every
  connection carries the Lance bootstrap. Engine rules the projection tests forced (also in code
  comments): lance rejects filtered UPDATEs → lifecycle folds are matched-only
  `MERGE ... USING (SELECT unnest($ids))`; inserts write `superseded_by = ''` and empty-blob
  evidence (store readers assume non-null); TIMESTAMPTZ binds `DateTimeOffset`, never a naive
  `DateTime` (session-timezone shift). SchemaRegistry's `ProjectionsTests` is stale against the
  current package (`[Skip("Flaky")]`, wrong `TClient`) — don't copy it verbatim; Kontext's
  `KontextMemoryProjectionTests` is the working template.
- **PROBED 2026-08-03: the raw Quack `Appender` (duckdb_appender_create) CAN write lance-catalog
  tables** — create+append+flush+read validated live against `ldb.main`; pinned as regression test
  `AppenderLanceProbeTests` (Kurrent.Kontext.Tests). Only via `USE ldb` session redirection: the C
  API has no catalog slot, qualified names parse as literal table names, and Quack does not wrap
  `duckdb_appender_create_ext`. The earlier belief "appender can't reach lance" is REFUTED — never
  reassert it unprobed. Still open: `Row.Add` lacks LIST/ARRAY/FLOAT[N] overloads (tags/supersedes/
  cited_memory_ids/embedding can't ride the appender yet), TIMESTAMPTZ semantics via `Add(DateTime)`,
  and flush→lance-commit granularity — BOTH RESOLVED same day: BLOB rides `Add(ReadOnlySpan<byte>)`
  into lance length-exact, and one appender flush is ONE lance commit regardless of row count
  (100 rows/1 flush → 1 manifest; 3 single SQL INSERTs → 3) — commits scale with flushes, the
  batch-amortization measured. `AppenderLanceProbeTests` pins all of it. BufferedView is
  secondary-indexing READ-path machinery
  (unflushed-row visibility for native scans via `get_buffered_rows` macro union); Kontext doesn't
  need it — the candidate primitive is the raw `Appender`.
- **DECIDED 2026-08-03 (evening): the memories write path is IConsumer-direct, NO
  ProcessingModule/Projection layer** — Sérgio's call after the module proved to be ceremony
  (RecordContext/ProcessorMetadata fabrication). Pipeline: `consumer.Records` →
  `ReadBatched` (Kontext's own extension, `Infrastructure/AsyncEnumerableExtensions.cs`) → per
  batch: batch-embed retains → insert leg → folds → checkpoint AFTER the data. EVOLVED 2026-08-04
  (all landed, 98/98 green): the whole batch is ONE facet-guarded MERGE — inserts, content
  refreshes, and ALL lifecycle folds in a single statement (one lance commit, one row-write per
  id; conditional WHEN arms probed OK on lance; statements inside a duck tx each commit
  SEPARATELY on lance, so multi-statement never had atomicity there). Aggregation = one
  `PendingMemory` per id (Touch/Retain/Supersede/Retract/Recall verbs; embedding rides the
  object). Ownership: `KontextMemoryProjectorService` owns connection + per-batch tx +
  checkpoint; `KontextMemoryWriter(connection, embeddings, EmbeddingGenerationOptions)` only
  turns batches into the statement. Checkpoints: Kontext-owned `KontextCheckpointStore` — Quack
  typed statements, table `checkpoints(key VARCHAR PK, position BIGINT, timestamp BIGINT
  unix-ms)`, connection per call, monotonic guard (stale store = no-op). ALSO REFUTED
  2026-08-04: "parameters don't prepare across a multi-statement batch" — DuckDB.NET 1.5.3
  binds them fine (probed); stale seed comments still cite it. COMMENT RULE reaffirmed hard:
  no probe dates, no changelog, no roadmap in code comments — hazards and behavior only.
  **v2 = quack appender via
  `Append<MemoryRowArgs, MemoryRowBinder>` for the full-database rebuild path** (secondary-indexing
  rates; needs the high-water skip filter since the appender inserts blindly).
  **UNBLOCKED 2026-08-05: Sérgio merged the Quack appender PRs; `0.0.0-alpha.217` is published on
  both feeds.** The shipped API is `Add(ReadOnlySpan<T>, CollectionType { List | Array })` overloads
  on `Appender.Row` (float/double/int/long/string?/bool/… element types) plus
  `AddList(ReadOnlySpan<byte>, ReadOnlySpan<Range?>)` — NOT the anticipated `AddList`/`AddArray`
  pair. `embedding FLOAT[N]` rides `Add(ReadOnlySpan<float>, CollectionType.Array)`; the four
  VARCHAR[] columns ride `Add(ReadOnlySpan<string?>, CollectionType.List)`. The earlier
  schema-reshape analysis is moot. Pin bumped to `alpha.217` 2026-08-05 (Quack + Quack.Arrow);
  `AppenderLanceProbeTests` PASSED against it — the appender capability is verified live.
  **DISCOVERED during that run: the working tree's own `DuckDB.NET.Data.Full` 1.5.3→1.5.5 bump
  (Sérgio's dependency refresh, NOT the Quack bump — both Quack versions declare 1.5.3; CPM
  overrides) moves the engine to v1.5.5, and the freshly auto-installed lance build for 1.5.5
  CHANGES SEARCH SEMANTICS**: `lance_vector_search`/`lance_fts` now REQUIRE an explicit `filter`
  when `prefilter=true` on "namespace-backed tables" (local attaches now count) — 42 search
  failures across Kurrent.Kontext.Tests + DuckLance.Tests; one DuckLance capability-gate test
  (`Expected to throw NotSupportedException`) inverted, evidence the filter/containment surface
  the 2026-08-02 pushdown research targeted has landed. The duckdb-lance KB doc's §9 upgrade
  tripwires fired — the whole prefilter+spliced-WHERE convention needs re-probing before the
  search layer is adapted. Decision pending: adapt to 1.5.5 vs revert the DuckDB.NET pin.
- **DECIDED 2026-08-03: every memories timestamp column is BIGINT Unix epoch MILLISECONDS** —
  Sérgio's ruling ("we're not going to use timestampZ; Unix timestamp for all dates"); TIMESTAMPTZ
  retired from the read model (schema DDL, projection writes, store reads/sorts, all test seeds —
  landed same day, 70/70 green). Plain numbers carry no session-timezone semantics and ride the
  appender's `Add(long)`. AgentSessionImporter's session-capture tables intentionally NOT
  converted (separate subsystem, not the appender target) — flagged, awaiting his call.
- **DECIDED 2026-08-03 (late): the retain insert leg is a PARTITIONED UPSERT, not insert-if-absent**
  — Sérgio's ruling ("MemoryRetained must be an upsert so we can replay"). The matched arm
  refreshes ONLY the retain-owned content columns (incl. `embedding`); the fold-owned lifecycle
  columns (is_retracted/retracted_at, is_superseded/superseded_at/superseded_by,
  last_accessed_at) are never touched by a retain — no resurrection on replay, folds re-assert
  their own columns. Consequence: **in-place replay re-embeds; embedding-model migration no longer
  requires dropping the table** — the older "rebuild re-embeds = drop first" note is OBSOLETE.
  Pinned by `KontextMemoryWriterTests.retain_replay_refreshes_the_embedding_in_place`.
- **DECIDED 2026-07-19: single lance table stays** (today's KontextStore shape). The duck/lance
  split is SHELVED after the capability review showed it buys stamp economics and listing
  ergonomics, not capability — revisit only if stamp/touch volume or listing needs start hurting;
  migration path between shapes is validated (CTAS / INSERT..SELECT). The two split-spikes
  (cross-catalog JOIN, one-txn-one-catalog) are moot for now. Stream-first/write-through remains an
  orthogonal, still-open question.
