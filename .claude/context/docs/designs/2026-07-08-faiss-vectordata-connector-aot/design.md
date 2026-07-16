---
title: A Native-AOT FAISS Connector for Microsoft.Extensions.VectorData
status: superseded
authors: [sergio]
date: 2026-07-08
tags: [vector-search, faiss, aot, semantic-kernel, vectordata, multi-tenant]
related: [2026-07-08-secure-workspace-segmentation-input-spec]
superseded_by: 2026-07-08-lancedb-c-dotnet-aot-bindings
---

> **Superseded (2026-07-08).** FaissNet exposes no filtered search, no persistence, and is x64-Windows
> only, so FAISS would mean binding `libfaiss_c` from scratch *and* inheriting FAISS's one-selector-per-
> batch + no-GPU filter limits + a mandatory separate metadata store. We pivoted to LanceDB, whose
> official `lancedb-c` C bindings provide native, pre-filtered SQL search. Replaced by
> [2026-07-08-lancedb-c-dotnet-aot-bindings](../2026-07-08-lancedb-c-dotnet-aot-bindings/design.md). This
> document is retained as the rejected-alternative record (the "why not FAISS" reasoning).

## Context

We want secure per-workspace (`workspace_id`) segmentation of embedding vectors, using FAISS,
in a full .NET 10/11 Native-AOT service. The originating spec proposed doing this with
`Microsoft.SemanticKernel.Connectors.Faiss`. That package **does not exist for .NET** — the
Semantic Kernel FAISS connector ships Python-only, and the C# documentation page for it reads
"Not supported at this time." (See [References](#references).)

So the actual work is: **build our own FAISS connector for the `Microsoft.Extensions.VectorData`
(MEVD) abstractions**, AOT-clean, that plugs into the same `VectorStore` /
`VectorStoreCollection<TKey,TRecord>` surface every other connector uses. This gets us the
provider-swap portability the spec wanted (Qdrant/Azure AI Search later = a one-line DI change)
*and* keeps FAISS as the local, in-memory backend today.

Two facts shape the whole design:

1. **FAISS stores vectors and `int64` ids only — no metadata.** Every real FAISS integration
   (including the Python SK connector) pairs the FAISS index with a *separate* record store and
   uses FAISS purely as the vector index. Our connector must own that record store.
2. **Native AOT bans runtime codegen.** `Expression.Compile()`, reflection-emit, and
   `Type.MakeGenericType` are out (IL3050 / `RequiresDynamicCode`). This dictates how we do both
   the P/Invoke layer and the LINQ filter evaluation.

> Terminology note: despite the `Microsoft.SemanticKernel.Connectors.*` naming of the shipped
> providers, the MEVD abstractions "have nothing to do with Semantic Kernel and are usable
> anywhere in .NET." We build against `Microsoft.Extensions.VectorData.Abstractions` directly.

## Design

### Component overview

```
┌──────────────────────────────────────────────────────────────────────┐
│  FaissVectorStore : VectorStore                                        │
│   • GetCollection<TKey,TRecord>(name, definition?)  → construct only   │
│   • CollectionExistsAsync / ListCollectionNamesAsync                   │
│   • owns the on-disk root dir; one subdir per collection               │
└───────────────┬────────────────────────────────────────────────────────┘
                │ constructs
                ▼
┌──────────────────────────────────────────────────────────────────────┐
│  FaissCollection<TKey,TRecord> : VectorStoreCollection<TKey,TRecord>   │
│   ├─ FaissIndexHandle            (SafeHandle over libfaiss_c)          │
│   ├─ record store: Dictionary<long, TRecord>   (metadata + payload)   │
│   ├─ key map:      Dictionary<TKey,long> + long→TKey                   │
│   ├─ IFaissRecordMapper<TKey,TRecord>   (source-generated, no refl.)  │
│   ├─ FaissFilterInterpreter             (tree-walks Expression, no    │
│   │                                       Compile())                   │
│   └─ ReaderWriterLockSlim               (search=read, upsert/del=write)│
└──────────────────────────────────────────────────────────────────────┘
                │ P/Invoke (LibraryImport, source-generated marshalling)
                ▼
        libfaiss_c  (native, shipped per-RID)
```

Keep both classes **`sealed`** (MEVD guidance: use a decorator to override behaviour, don't
subclass). Optional settings go through an options class deriving from `VectorStoreCollectionOptions`.

### 1. The MEVD contract we must satisfy

A full connector implements exactly two types — `VectorStore` and
`VectorStoreCollection<TKey,TRecord>` (the latter already implements `IVectorSearchable<TRecord>`).
Method obligations that bite us specifically:

- `VectorStore.GetCollection` **must not** probe existence — construct and return. Existence is a
  separate `CollectionExistsAsync` call.
- `DeleteAsync` (single and batch) **must succeed when the record is absent** — only throw on real
  failures.
- Batch `Get`/`Upsert`/`Delete` have base implementations that call the single-item versions in
  serial. FAISS `add`/`search` are natively batched, so we **override** the batch paths for the
  vector operations.
- Search entry point is `SearchAsync<TInput>(TInput value, int top, VectorSearchOptions<TRecord>?, ct)`
  returning `IAsyncEnumerable<VectorSearchResult<TRecord>>` directly (the old
  `VectorizedSearchAsync` + `.Results` wrapper is gone). `TInput` is a `ReadOnlyMemory<float>`
  embedding in our low-level case; if an `IEmbeddingGenerator` is configured MEVD hands us a
  vector before we're called.
- Validation uses `ArgumentException` / `ArgumentNullException`; operational failures use the MEVD
  `VectorStoreException` family.

The authoritative per-method checklist is the "How to build your own Vector Store connector" page
(see References) — treat it as the acceptance criteria.

### 2. Native binding: thin C-API layer, `LibraryImport`, `SafeHandle`

FAISS is C++ with a stable C API (`c_api` / `libfaiss_c`). We bind **only** the C API — never
C++/CLI, which is Windows-only and not AOT-compatible.

- **`LibraryImport`, not `DllImport`.** Source-generated marshalling is compile-time, so it works
  under Native AOT and trimming with no runtime IL-stub cost.
- **Index handles are `SafeHandle` subclasses** (`FaissIndexHandle`, `FaissIdSelectorHandle`,
  `FaissSearchParametersHandle`) so native lifetimes are deterministic and exception-safe.
- **Any callbacks use `[UnmanagedCallersOnly]` static methods + function pointers**, never marshalled
  delegates. (FAISS itself needs none for our path; this is a rule for any future extension.)
- **Native binaries ship per-RID.** FAISS publishes no official NuGet with natives; this is the
  single biggest packaging cost. Plan: a `runtimes/<rid>/native/` layout (`win-x64`, `linux-x64`,
  `linux-arm64`, `osx-arm64`, `osx-x64`) built from FAISS via CMake in CI, or take a dependency on
  the community `Faiss.NET.Native` meta-package if its RID coverage and version are acceptable.
  **This must be settled before implementation** (see Open Questions).

The C entry points we need are small: `faiss_index_factory` / `faiss_read_index` / `faiss_write_index`,
`faiss_Index_add_with_ids`, `faiss_Index_search` and `faiss_Index_search_with_params`,
`faiss_Index_remove_ids`, and the selector constructors `faiss_IDSelectorBitmap_new` /
`faiss_IDSelectorBatch_new` plus `faiss_SearchParameters_new` (setting the `sel` field).

### 3. Record mapping without reflection — a source generator

FAISS holds a vector + `int64` id; everything else (the tenant id, payload text, other filterable
fields) lives in *our* record store. To move data between `TRecord` and that store we need to read
the key, read/write the vector, and read the filterable data properties. Doing that with
`PropertyInfo.GetValue` is runnable under AOT but **trim-unsafe** (members must be preserved) and
allocates.

Decision: ship a **Roslyn source generator** that, for each record annotated with
`[VectorStoreKey]` / `[VectorStoreData]` / `[VectorStoreVector]`, emits a static
`IFaissRecordMapper<TKey,TRecord>` with strongly-typed accessors:

```csharp
// generated
public static ReadOnlyMemory<float> GetEmbedding(KnowledgeBaseEntry r) => r.Embedding;
public static ulong               GetKey(KnowledgeBaseEntry r)       => r.Id;
public static object?             GetData(KnowledgeBaseEntry r, string name) => name switch {
    "WorkspaceId" => r.WorkspaceId,
    "TextContent" => r.TextContent,
    _ => throw ...
};
```

Zero reflection, zero codegen, fully trim/AOT-clean — the sanctioned alternative to reflection in
this codebase. Fallback for records the generator can't see: a caller-supplied record definition
carrying accessor delegates, with the reflection path clearly annotated `[RequiresUnreferencedCode]`
so the AOT cost is opt-in and visible.

### 4. Keys and metrics

- **Key → FAISS id.** FAISS ids are `int64`. Maintain a bijective `TKey ↔ long` map, assigning
  **monotonically increasing** ids on first upsert. Monotonic/sequential ids make `IDSelectorBitmap`
  compact and fast (see §5). `TKey` may be `ulong`, `string`, or `Guid`; the map is the only place
  the original key type matters.
- **Distance function → FAISS metric.** From `[VectorStoreVector(Distance = …)]`:
  cosine → L2-normalize on upsert + `METRIC_INNER_PRODUCT`; dot product → `METRIC_INNER_PRODUCT`;
  Euclidean → `METRIC_L2`. Normalize both orientations into `VectorSearchResult.Score` so callers
  get a consistent "higher = closer" score regardless of metric (L2 is ascending distance, IP is
  descending similarity — we invert L2).

### 5. Secure filtering — the actual isolation mechanism

MEVD filters are `Expression<Func<TRecord,bool>>` (the old `VectorSearchFilter().EqualTo(...)` API
is `[Obsolete]`). The tenant filter arrives as `options.Filter = r => r.WorkspaceId == currentTenant`.

Flow, and why it is a **pre-filter** (correct for tenant isolation):

1. **Interpret the filter** against our in-memory record store with a `FaissFilterInterpreter` — an
   `ExpressionVisitor` that tree-walks the supported operators (`==`, `!=`, `&&`, `||`, `<`/`>`,
   member access, constant, `Contains`). **No `Expression.Compile()`** — that is the AOT-illegal
   part the spec's mental model glossed over. Tree-walking is codegen-free and AOT-legal.
2. The interpreter yields the set of FAISS ids whose records satisfy the filter.
3. Build an **`IDSelectorBitmap`** (1 bit/id — compact and fast for our sequential ids; falls back
   to `IDSelectorBatch` when the allowed set is sparse) and attach it to `SearchParameters.sel`.
4. `faiss_Index_search_with_params` evaluates vector distances **only over the selected subset**.
   Two workspaces with byte-identical embeddings never cross, because the other tenant's ids are
   not in the bitmap — this is the accurate version of the spec's "bitset" claim.

FAISS filtering caveats we must encode as constraints, not gloss over:

- **Use a Flat index (`IndexFlatIP` / `IndexFlatL2`) for the isolation-critical path.** ANN indexes
  (HNSW, IVF) apply the selector *during* graph/cell traversal, which can **under-return** (fewer
  than `top`, or empty) — there are open FAISS issues for `IndexHNSWFlat` + `IDSelectorBitmap`
  returning empty results, and `IDSelector` is unreliable on GPU. Flat is exact and selector-safe.
  Offer IVF/HNSW as opt-in with a documented recall caveat.
- **Validate ids before building a selector.** Out-of-range ids passed to a selector can cause a
  native access violation (0xC0000005) — a crash, not an exception. The `TKey↔long` map guarantees
  in-range ids by construction; assert it.

### 6. Security model — honest boundaries (corrects the input spec)

The input spec claimed metadata filtering gives "Zero (Guaranteed)" leakage "equal to physical
separation." That is wrong and must not survive into the design:

- A single shared index **fails open**: one code path that forgets the filter, or builds it from an
  attacker-influenced value, exposes the *entire* corpus. Physical (index-per-tenant) separation
  **fails safe**: the wrong file simply has no other tenant's data.
- Therefore the connector supports **both**, and we recommend **defense in depth**:
  - **Collection-per-tenant** (one FAISS index + record store per `workspace_id`) for hard tenant
    boundaries — this is just "a collection per tenant" in MEVD terms and is the strong default for
    untrusted multi-tenancy.
  - **Metadata pre-filter within a collection** for softer partitioning (e.g. per-user scoping
    inside one tenant) and for cross-partition admin queries.
- Consumer-side, the tenant filter must be injected centrally (an ASP.NET Core endpoint filter /
  middleware that reads `workspace_id` from the validated JWT and *always* appends
  `r => r.WorkspaceId == tenant`), never hand-written per query. A missing filter is the whole risk;
  centralizing it is the mitigation. Do **not** read the tenant from a client-supplied header.

### 7. Threading, persistence, lifecycle

- **Concurrency.** MEVD expects collections to be thread-safe. FAISS tolerates concurrent searches
  but not add/remove concurrent with search. Guard with a `ReaderWriterLockSlim`: `SearchAsync`/`GetAsync`
  take the read lock, `UpsertAsync`/`DeleteAsync` take the write lock.
- **Delete.** Flat indexes support `remove_ids`; index types that don't get tombstoned in the record
  store + filtered out, with a periodic rebuild. `DeleteAsync` of an absent key is a no-op success.
- **Persistence.** `EnsureCollectionExistsAsync` loads or creates `<root>/<collection>/`:
  `index.faiss` (via `faiss_write_index`/`faiss_read_index`), `records.json` (the metadata store,
  serialized with a **System.Text.Json source-generated** `JsonSerializerContext`), and `keymap.json`
  (the `TKey↔long` map). Writes are atomic (temp file + rename). `EnsureCollectionDeletedAsync`
  removes the directory.
- **Native lifetime.** `SafeHandle`s release the index/selector/params; the collection's `Dispose`
  flushes and releases.

### 8. Packaging

- `Faiss.Extensions.VectorData` — the managed connector (`<IsAotCompatible>true</IsAotCompatible>`).
- `Faiss.Extensions.VectorData.Generators` — the source generator (netstandard2.0, per the repo's
  source-generator exception to the net10-only rule).
- Native `libfaiss_c` per RID (own CI build or `Faiss.NET.Native` dependency — Open Question).
- DI: `services.AddFaissVectorStore(root)` returning the same `VectorStore` abstraction, so swapping
  to Qdrant/Azure AI Search later is a registration change with the filter/search code untouched —
  the portability win the original spec correctly identified.

## Alternatives Considered

- **Use the Python SK FAISS connector.** Rejected: we're a .NET 10/11 AOT service; a Python sidecar
  reintroduces a process boundary and a second runtime, defeating the "single hot in-memory index"
  goal.
- **Swap FAISS for a shipped .NET MEVD connector (Qdrant / Postgres+pgvector / InMemory).** Viable
  and lower-effort — Qdrant does server-side filtered ANN well. Rejected as the *primary* path only
  because the explicit ask is native embedded FAISS with no external service; but the connector
  abstraction means this stays a drop-in fallback, and **InMemory is the recommended prototyping
  backend** while the native build is stood up.
- **C++/CLI wrapper (FaissNet ≤0.5.4 style).** Rejected: Windows-only and not AOT-compatible. The
  C-API + `LibraryImport` path is the only AOT-clean option.
- **Reflection-based record mapping (what shipped connectors mostly do).** Rejected for the hot path:
  trim-unsafe and allocating. Source-generated mappers are the AOT-first choice here, consistent with
  the rest of this codebase.
- **`Expression.Compile()` for filters (implied by the input spec's model).** Rejected — illegal
  under AOT (IL3050). Tree-walking interpretation is mandatory.

## Open Questions

- **Native distribution:** build `libfaiss_c` per-RID in our own CI, or depend on
  `Faiss.NET.Native`? Need to confirm its RID matrix (esp. `linux-arm64`, `osx-arm64`), FAISS version,
  and AOT-friendliness before committing. This gates everything downstream.
- **Index type default:** Flat is exact and selector-safe but O(n) per query. At what corpus size do
  we need IVF/HNSW, and can we accept their filtered-recall caveats — or do we prefer
  collection-per-tenant (smaller per-index n) to keep Flat viable longer?
- **Filter operator coverage:** which subset of LINQ does the interpreter support at v1? Proposed
  minimum: `==`, `!=`, `&&`, `||`, comparisons, and `IEnumerable.Contains` (for tag/`in` filters).
- **Multi-vector records:** support multiple `[VectorStoreVector]` properties (one FAISS index each)
  in v1, or single-vector only?
- **Is this connector for the Kontext/Engram memory workstream, or a standalone library?** Affects
  namespace, repo home, and whether it should live under `src/` here at all.

## References

- [Build your own Vector Store connector (C#)](https://learn.microsoft.com/semantic-kernel/concepts/vector-store-connectors/how-to/build-your-own-connector) — authoritative connector acceptance criteria
- [`VectorStoreCollection<TKey,TRecord>`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.vectordata.vectorstorecollection-2) · [`VectorStore`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.vectordata.vectorstore) (abstractions v10.7.0, net-11.0-pp)
- [FAISS connector (Preview)](https://learn.microsoft.com/semantic-kernel/concepts/vector-store-connectors/out-of-the-box-connectors/faiss-connector) — C# "Not supported at this time"; Python-only
- [Out-of-the-box connectors matrix](https://learn.microsoft.com/semantic-kernel/concepts/vector-store-connectors/out-of-the-box-connectors/)
- [Vector Store changes — March 2025](https://learn.microsoft.com/semantic-kernel/support/migration/vectorstore-march-2025) (LINQ filters replace `VectorSearchFilter`) · [April 2025](https://learn.microsoft.com/semantic-kernel/support/migration/vectorstore-april-2025) (`VectorizedSearchAsync` → `SearchEmbeddingAsync`)
- [IL3050 / RequiresDynamicCode](https://learn.microsoft.com/dotnet/core/deploying/native-aot/warnings/il3050) — why `Expression.Compile()` is out under AOT
- [FAISS: setting search parameters for one query](https://github.com/facebookresearch/faiss/wiki/Setting-search-parameters-for-one-query) · [`SearchParameters` C++ API](https://faiss.ai/cpp_api/struct/structfaiss_1_1SearchParameters.html) — `IDSelector` via `SearchParameters.sel`
- FAISS filtering caveats: [HNSW + IDSelectorBitmap empty results (#3186)](https://github.com/facebookresearch/faiss/issues/3186) · [IDSelectorArray access violation (#3156)](https://github.com/facebookresearch/faiss/issues/3156) · [IDSelector on GPU (#3924)](https://github.com/facebookresearch/faiss/issues/3924)
- .NET FAISS bindings: [FaissNet](https://github.com/fwaris/FaissNet) · [FaissMask](https://github.com/andyalm/faissmask) · [Faiss.NET.Native](https://libraries.io/nuget/Faiss.NET.Native) · [FAISS native NuGet request (#2609)](https://github.com/facebookresearch/faiss/issues/2609)
