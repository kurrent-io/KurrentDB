---
title: MEVD Connector for the Kontext USearch Vector Store
status: draft
authors: [sergio]
date: 2026-07-03
tags: [kontext, vector-search, usearch, mevd, microsoft-extensions-ai]
related: [2026-06-12-memory-model-v2]
---

## Context

Kontext's vector search is a bespoke stack: `IVectorStore` / `USearchVectorStore`
(`src/KurrentDB.Kontext/Indexing/USearch/`) — an LSM-style index (in-memory L0
`ActiveSnapshot` → sealed Int8-quantized HNSW segments via `Cloud.Unum.USearch`, background
leveled merges). Consumers (`Retriever`, `KontextService`, `EmbeddingCache`, MCP search/recall
tools) are coded directly against `IVectorStore`.

Two forces motivate an abstraction seam:

1. **Store swap-ability.** Consumers are coded directly against the bespoke `IVectorStore`,
   so changing the backing vector engine — for any collection, for any reason — rewrites
   every consumer today. The abstraction is the seam that makes the engine replaceable.
2. **Ecosystem alignment.** Embedding generation already uses
   `Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>>` throughout
   (local ONNX MiniLM, OpenAI, Ollama, Vertex, Bedrock). `Microsoft.Extensions.VectorData`
   (MEVD) is the companion abstraction for the store side; adopting it completes the pair
   and unlocks the dotnet AI extensions ecosystem (SK, MEAI evaluation, etc.).

MEVD status: `Microsoft.Extensions.VectorData.Abstractions` **10.7.0** (aligned with our
`Microsoft.Extensions.AI.Abstractions` 10.5.1 — same dotnet/extensions release train).
The core abstractions are GA. `VectorStoreCollection<TKey, TRecord>` implements
`IVectorSearchable<TRecord>`; a connector is two classes:
`{Db}VectorStore : VectorStore` and `{Db}Collection<TKey,TRecord> : VectorStoreCollection<TKey,TRecord>`.

## Design

### Fit assessment — what maps cleanly and what doesn't

| MEVD member                                   | USearch store today                            | Verdict |
|-----------------------------------------------|------------------------------------------------|---------|
| `SearchAsync(input, top)` → results + score   | `Search(vector, limit)` → ordered `ulong` ids  | ✅ needs scores surfaced (NearestMatches already has distances) |
| `GetAsync(TKey)`                              | `TryGet(ulong, out vector)`                    | ✅ direct |
| `UpsertAsync(TRecord)`                        | `Add(ulong, logPosition, vector)` — append-only, dedup-on-merge | ⚠️ last-write-wins works; log-position watermark is extra-contractual |
| `DeleteAsync(TKey)`                           | none — no tombstones                           | ❌ `NotSupportedException` (documented limitation) |
| `GetAsync(filter expression, top)`            | no data properties, no filtering               | ❌ `NotSupportedException` |
| `EnsureCollectionExists/DeletedAsync`         | workspace lifecycle mounts/unmounts stores     | ⚠️ exists = registry lookup; create/delete stay owned by `WorkspaceRunner` |
| thread-safety expectation ("thread-safe unless documented") | single-writer `Add`/`Flush`, lock-free reads | ⚠️ adapter serializes writes (`SemaphoreSlim`); reads already safe |

None of the ❌/⚠️ items block adoption — MEVD explicitly tolerates connector limitations
(documented `NotSupportedException` is the prescribed pattern) — but they mean our connector
is an *internal pragmatic subset*, not a publishable full-compliance connector.

### Shape

New folder `src/KurrentDB.Kontext/Indexing/VectorData/`:

```csharp
// Record model — fixed shape; the store is a pure ANN index, payloads hydrate from
// the log / FTS as today. TRecord : class satisfied by a sealed record.
public sealed record VectorEntry {
    public required long Key { get; init; }                  // record log position or stream-name hash
    public required ReadOnlyMemory<float> Vector { get; init; }
    public long LogPosition { get; init; }                   // commit log position — durability watermark input (write path only)
}

public sealed class USearchVectorStore : VectorStore { ... }             // per-workspace
public sealed class USearchCollection : VectorStoreCollection<long, VectorEntry> { ... }
```

- **Collections map to index kinds.** One MEVD `VectorStore` per workspace; collection names
  are the four kinds (`records`, `memory`, `records.streams`, `memory.streams`), resolved
  through the existing `StoreRegistry<IVectorStore>` by `WorkspaceIndex`. `GetCollection`
  constructs the adapter without existence checks (per MEVD connector requirement 1.2);
  `ListCollectionNamesAsync` enumerates the registry.
- **Pragmatic generics.** `GetCollection<TKey,TRecord>` type-checks: only
  `<long, VectorEntry>` is supported; anything else throws (keys are `long` at the API —
  the log-position domain type — and cast to USearch's native `ulong` key only at the
  engine boundary). No attribute/definition-driven
  model building, no `GetDynamicCollection` — internal connector, fixed schema.
- **Scores.** Extend the internal search to return `(long Key, float Score)` pairs
  (cosine similarity; `NearestMatches` already computes this — it is currently dropped at
  the `IVectorStore.Search` boundary). `VectorSearchResult<VectorEntry>` carries it.
- **Query embedding.** `SearchAsync<TInput>` accepts `ReadOnlyMemory<float>` directly;
  optionally accepts `string` when an `IEmbeddingGenerator` is supplied via the options
  class — slots straight into the existing `EmbeddingService`/`EmbeddingCache`.
- **Durability watermark via `GetService`.** `Flush(logPosition, force) → logPosition`
  (the commit log position — the existing engine's `TFPos` usage is considered a mistake
  and is replaced by the single log position going forward) is KurrentDB-specific and has
  no MEVD equivalent. The adapter exposes the underlying
  `IIndexStore` through `GetService(typeof(IIndexStore))` — MEVD's sanctioned escape hatch
  for provider-specific features. `EmbeddingSubscription` checkpointing keeps working.
- **Exceptions.** Wrap store failures in `VectorStoreException` per the standard-exceptions
  requirement; argument validation stays `ArgumentException`/`ArgumentNullException`.
- **Naming — decided (2026-07-03).** The existing engine class is renamed (new name TBD,
  e.g. `UsearchLsmIndex`) and kept alongside so both implementations can be compared; the
  MEVD adapter takes the conventional `USearchVectorStore` name per Microsoft's connector
  naming guidance.

### Phasing

1. **Read-side seam (low risk, immediate value).** Add the package, implement the adapter
   pair, surface scores from the engine, and move `Retriever`/`KontextService` search paths
   to `IVectorSearchable<VectorEntry>` / `VectorStoreCollection`. Ingestion untouched.
2. **Write path through the abstraction.** `EmbeddingSubscription` upserts `VectorEntry`
   (with `LogPosition`) via `UpsertAsync`; adapter serializes writes; watermark/`Flush` via
   `GetService(IIndexStore)`. After this, `IVectorStore` is consumed nowhere but the adapter.
3. **The payoff.** If the backing engine for any collection ever changes, the swap is a new
   connector behind the same abstraction — flip the affected collections to it; consumers
   don't change. A different backend can also lift the documented limitations (real delete,
   filterable data properties) where USearch could not.

### Effort

Phase 1 is small: one options class, two sealed adapter classes (~300–400 lines), a
`(key, score)` extension to the internal search path, and mechanical consumer changes in
`Retriever`/`EmbeddingCache`. Phase 2 is comparable. No storage-format changes at any phase.

### Candidate backend: LanceDB (investigated 2026-07-03)

LanceDB (embedded, Rust core, Apache-2.0, local-disk, no server) natively implements
essentially everything the hand-built USearch LSM layer exists to work around, plus what it
can't do at all:

| Hand-built today                          | LanceDB equivalent                                     |
|-------------------------------------------|--------------------------------------------------------|
| L0 buffer (volatile) + seal threshold     | New rows land as durable fragments, searchable immediately via flat scan over the unindexed tail |
| `SegmentMerger` background compaction     | `Optimize()` — incremental index merge + fragment compaction + version pruning; OSS leaves cadence to the caller (schedule/trigger; rule of thumb ~100k new rows), monitor `index_stats().num_unindexed_rows` |
| manifest + `.keys` sidecars               | Versioned Lance manifest; real schema, keys enumerable |
| Min-watermark checkpoint + replay probes  | Writes durable on commit — checkpoint after the call returns |
| impossible: delete/update                 | `Delete`/`Update`/`MergeInsert` (true upsert)          |
| Lucene (separate FTS store) + C# RRF      | Native BM25 FTS + hybrid search with RRF rerankers in-engine |

MEVD fit: `IKeywordHybridSearchable<TRecord>` exists in the abstractions (10.7.0), so a
`LanceDbCollection` could expose vector, filtered, *and* hybrid search behind standard
interfaces — connector #2 next to the USearch connector, directly comparable.

PoC (2026-07-03, `.project/.scratch/lancedb-poc/lancedb-poc.cs`, file-based C# app, osx-arm64):
**all steps passed** — connect, Arrow-schema table create, batched adds, FTS + HNSW-SQ index
creation, vector search (exact hit, distance 0), filtered search, BM25 FTS, hybrid + RRF,
and `Optimize` (post-optimize search returned rows from the previously unindexed tail batch,
confirming incremental index merge). Rows come back as `Dictionary<string, object>` with
`_distance` / `_score` / `_relevance_score` pseudo-columns.

Risks (binding, not engine): there is **no official .NET SDK** — the `LanceDB` NuGet
(lennylxx/lancedb-csharp, v2.4.1 May 2026) is a single-maintainer community wrapper
(9 stars, author-stated "built in two weeks with AI"), P/Invoke over the Rust crate with a
Tokio runtime pool. API surface looks complete (`Optimize`, `MergeInsert`, FTS, hybrid +
rerankers) but ships **no linux-arm64 natives** (KurrentDB ships arm64 images). Official
`lancedb/lancedb-c` FFI bindings exist as a basis for owning a thin interop layer instead.
Disk math to verify: Lance keeps the float32 vector column (~1,536 B/vector at 384 dims) +
index, vs ~384 B/vector in today's Int8 segments. Spike before committing: arm64 build,
ingest throughput, query latency vs unindexed-tail growth, `Optimize` duration at scale,
FFI robustness (cancellation, Arrow memory ownership), in-process behavior next to the
server.

## Alternatives Considered

- **Full published-connector compliance** (attribute-driven models,
  `VectorStoreCollectionDefinition`, dynamic collections, generic TRecord mapping): pure
  overhead for an internal, fixed-schema index; the reflection-based model builder is also
  the part flagged `RequiresUnreferencedCode`. Rejected.
- **Store payloads in the vector store** so `GetAsync` returns full documents: contradicts
  the deliberate architecture — the log is the source of truth and hydration goes through
  FTS/record reads; duplicating payloads bloats the segments (the Int8 quantization win
  exists precisely because segments hold only vectors). Rejected.
- **Implement only `IVectorSearchable<TRecord>`**: the minimal search seam, but it gives no
  upsert path and no collection management, so the store swap would still require touching
  ingestion. Kept as the *consumer-facing* type where possible, but the connector pair is
  what makes the swap real. Rejected as the whole answer.
- **Skip MEVD, keep bespoke `IVectorStore` and write alternative implementations of it later**:
  workable, but keeps us off the ecosystem (SK memory, MEAI tooling) and the bespoke
  interface would grow to re-invent what MEVD already standardizes (scores, options,
  batching, exceptions). Rejected.

## Open Questions

- Should `EmbeddingCache`'s direct `TryGet` cross-workspace reuse move to `GetAsync`, or is
  it a legitimate engine-level concern that stays below the abstraction?
- Is the log position acceptable as a `VectorEntry` property (write-path input, never returned on
  read), or should the write path keep a bespoke signature until Phase 3 forces the issue?
