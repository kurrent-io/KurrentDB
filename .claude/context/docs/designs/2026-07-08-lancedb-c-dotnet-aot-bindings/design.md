---
title: A Native-AOT Microsoft.Extensions.VectorData Connector for LanceDB (over lancedb-c)
status: draft
authors: [sergio]
date: 2026-07-08
tags: [vector-search, lancedb, aot, interop, pinvoke, arrow, vectordata, connector, multi-tenant]
supersedes: 2026-07-08-faiss-vectordata-connector-aot
related: [2026-07-08-faiss-vectordata-connector-aot]
---

## Context

**The deliverable is a connector.** The goal from the outset is secure per-workspace (`workspace_id`)
segmentation of embedding vectors via pre-filtered metadata vector search, exposed as a
`Microsoft.Extensions.VectorData` (MEVD) connector — the same `VectorStore` /
`VectorStoreCollection<TKey,TRecord>` surface every shipped provider implements — so consumer code and
the swap-to-another-backend story are standard. Everything below (P/Invoke, Arrow bridge, filter
translation) is a *layer of that connector*, not a substitute for it.

Foundation decision (see [Alternatives](#alternatives-considered) and the superseded
[FAISS doc](../2026-07-08-faiss-vectordata-connector-aot/design.md)): build on the **official**
[`lancedb/lancedb-c`](https://github.com/lancedb/lancedb-c) C FFI. LanceDB is embedded (Rust core, Lance
columnar format), stores metadata as native columns, and does **pre-filtered** vector search — the
isolation semantics we need, with no separate metadata store or id map.

The C header, `Cargo.toml`, and examples were read directly (cloned `lancedb-c` @ `v0.30.0`). Findings
below are from source, not memory; the [VERIFY items](#verify-items--resolved-from-source) I previously
flagged are now answered.

## Design

### Layering (the connector, top to bottom)

```
LanceDB.Extensions.VectorData            ← the CONNECTOR (the product)
  ├─ LanceDbVectorStore        : VectorStore
  ├─ LanceDbCollection<K,R>    : VectorStoreCollection<K,R>   (Get/Upsert/Delete/Search/EnsureCollection…)
  ├─ Filter translation:  Expression<Func<R,bool>>  →  lancedb_expr_*  (tree-walk, NO Expression.Compile)
  ├─ Record mapping:      R  ↔  Arrow RecordBatch    (source-generated, no reflection)
  ├─ Arrow bridge:        Apache.Arrow C Data Interface  (CArrowArray/CArrowSchema)
  └─ Native interop:      [LibraryImport] over lancedb.h  +  SafeHandle
        │  P/Invoke + Arrow C ABI (blocking calls)
        ▼
  liblancedb  (Rust cdylib @ v0.30.0; built per-RID)
```

### lancedb-c — verified API surface

- **Opaque handles + explicit lifecycle.** `LanceDBConnection`, `LanceDBTable`, `LanceDBQuery`,
  `LanceDBVectorQuery`, `LanceDBQueryResult`, `LanceDBExpr`, `LanceDBRecordBatchReader`, … each with a
  `lancedb_*_free`. → each maps to a `SafeHandle`.
- **Uniform error model.** Fallible calls return a `LanceDBError` enum (`0 = SUCCESS`, 1–22 codes) and
  take a trailing `char** error_message` out-param (freed with `lancedb_free_string`). → one
  `Check(err, msgPtr)` helper throwing a typed `LanceDbException`.
- **Data plane is the Arrow C Data Interface.** `FFI_ArrowSchema` / `FFI_ArrowArray` are the Arrow C ABI
  structs. Ingest: build a RecordBatch → `lancedb_record_batch_reader_from_arrow(array, schema, &reader)`
  → `lancedb_table_add` / `lancedb_table_merge_insert`. Results:
  `lancedb_vector_query_execute` → `lancedb_query_result_to_arrow(&arrays, &schema, &count)`.
- **Two filter APIs — we use the structured one.** A SQL string
  (`lancedb_vector_query_where_filter(q, "workspace_id = '…'")`) *and* a **DataFusion expression
  builder**: `lancedb_expr_column`, `lancedb_expr_literal_{string,i64,f64,bool}`,
  `lancedb_expr_binary(l, LANCEDB_BINARY_OP_*, r)`, `_and/_or/_not/_is_null/_in_list/_array_has`, applied
  via `lancedb_vector_query_df_filter(q, expr)`. The builder is what makes LINQ translation clean and
  injection-safe (see [filtering](#secure-workspace-filtering--the-centerpiece)).
- **CRUD + search primitives** map 1:1 to MEVD (table below).
- **Distances:** `L2 / COSINE / DOT / HAMMING`. **Index types:** scalar `BTREE`/`BITMAP` (fast
  `workspace_id` pre-filter), `FTS`, and vector `IVF_FLAT / IVF_PQ / IVF_HNSW_{PQ,SQ}`.

MEVD method → lancedb-c call:

| MEVD member | lancedb-c |
|---|---|
| `VectorStore.GetCollection` | construct `LanceDbCollection` (no I/O — per MEVD contract) |
| `CollectionExistsAsync` | `lancedb_connection_table_names` / probe `open_table` |
| `EnsureCollectionExistsAsync` | `lancedb_connection_open_table`; if missing `lancedb_table_create(schema, NULL)` **then create the indexes declared on the record** (scalar + vector) |
| `EnsureCollectionDeletedAsync` | `lancedb_connection_drop_table` |
| `UpsertAsync` | RecordBatch → `reader_from_arrow` → `lancedb_table_merge_insert(on=[keyColumn])` |
| `GetAsync(key)` | `lancedb_query_new` + `where "key = …"` + `execute` → import |
| `GetAsync(filter, top)` | `lancedb_query_new` + `df_filter(expr)` + `limit` + `execute` |
| `DeleteAsync(key)` | `lancedb_table_delete("key = …")` (succeeds if absent — MEVD requires it) |
| `SearchAsync(vector, top, options)` | `lancedb_vector_query_new` + `df_filter(options.Filter)` + `limit` + `distance_type` → `execute` → `result_to_arrow` |
| index build (from the record's attributes) | `lancedb_table_create_scalar_index(BTREE/BITMAP, [filter cols])`, `_create_vector_index(kind, [embedding])` — index kind + distance come from `[VectorStoreVector(...)]` / `[VectorStoreData(IsIndexed=true)]`, not a choice we make |

### Binding surface — scoped to the connector

We bind **only** what the MEVD connector calls — ~32 of the ~90 functions in `lancedb.h`, plus the
`LanceDBError` enum. **Indexing is part of that core set, not an add-on:** an unindexed vector store is a
regression, and MEVD expects collection creation to apply the indexes declared on the record. Grouped by
purpose:

- **Connect / store:** `lancedb_connect`, `_connect_builder_execute`, `_connect_builder_free`,
  `_connection_free`, `_connection_table_names` (+ `_free_table_names`), `_connection_open_table`,
  `_connection_drop_table`.
- **Collection schema / lifecycle:** `lancedb_table_create`, `_table_arrow_schema`, `_table_free`.
- **Write (Upsert):** `lancedb_record_batch_reader_from_arrow` (+ `_free` for error paths),
  `lancedb_table_merge_insert`.
- **Delete:** `lancedb_table_df_delete`.
- **Get (by key / filter):** `lancedb_query_new`, `_query_df_filter`, `_query_limit`, `_query_execute`,
  `_query_free`.
- **Search (vector):** `lancedb_vector_query_new`, `_df_filter`, `_limit`, `_offset`, `_column`,
  `_distance_type`, `_execute`, `_free`.
- **Results:** `lancedb_query_result_to_arrow`, `_query_result_free`, `_free_arrow_arrays`,
  `_free_arrow_schema`.
- **Filter builder (LINQ → expr):** `lancedb_expr_column`, `_literal_{string,i64,f64,bool}`, `_binary`,
  `_and`, `_or`, `_not`, `_is_null`, `_is_not_null`, `_in_list`, `_free`.
- **Indexing (core — applied at collection creation + used at search):** `lancedb_table_create_vector_index`,
  `_create_scalar_index`, the ANN search tuners `lancedb_vector_query_nprobes` / `_refine_factor` / `_ef`,
  and index management `_list_indices` / `_drop_index` / `_index_stats` / `_table_optimize`.
- **Utility:** `lancedb_free_string`.

**Explicitly NOT bound** (in the header, irrelevant to a vector-store connector): sessions & cache stats,
object-store registry/providers, namespaces, table rename / drop-all, the table-names pagination builder,
table key/value metadata, versions/time-travel, FTS index + config, the JSON-expression API
(`lancedb_expr_json_*`, `lancedb_json_matches`), `array_has` / `clone`, and `lancedb_run_on_stack`. Two
deliberate exclusions worth calling out: the one-shot `lancedb_table_nearest_to` (**no filter parameter →
can't do tenant-safe search**), and the SQL-string filters
(`lancedb_query_where_filter` / `lancedb_vector_query_where_filter`) — the structured expression builder
is used instead, so no predicate strings are ever assembled.

### Native interop — `LibraryImport` (grounded in `lancedb.h`)

The native lib is `lancedb` (`Cargo.toml` `[lib] name = "lancedb"`, `crate-type = ["cdylib", …]`), so it
resolves to `liblancedb.so` / `liblancedb.dylib` / `lancedb.dll` from `runtimes/<rid>/native/`.

`DllImport` cannot be used — its marshalling IL is generated at runtime (breaks AOT). `LibraryImport`
source-generates the marshalling at compile time, so it publishes clean under `PublishAot`.

```csharp
using System.Runtime.InteropServices;

internal static partial class Native {
    private const string Lib = "lancedb";

    // LanceDBConnectBuilder* lancedb_connect(const char* uri);
    [LibraryImport(Lib, EntryPoint = "lancedb_connect", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial ConnectBuilderHandle Connect(string uri);

    // LanceDBError lancedb_connect_builder_execute(LanceDBConnectBuilder*, LanceDBConnection**, char** err);
    // builder is consumed on success; take the raw handle, receive connection + error out-params.
    [LibraryImport(Lib, EntryPoint = "lancedb_connect_builder_execute")]
    internal static partial LanceDbError ConnectExecute(nint builder, out nint connection, out nint errorMessage);

    // LanceDBVectorQuery* lancedb_vector_query_new(const LanceDBTable*, const float* vector, size_t dim);
    [LibraryImport(Lib, EntryPoint = "lancedb_vector_query_new")]
    internal static partial VectorQueryHandle VectorQueryNew(TableHandle table, ReadOnlySpan<float> vector, nuint dimension);

    // LanceDBError lancedb_vector_query_limit(LanceDBVectorQuery*, size_t limit, char** err);
    [LibraryImport(Lib, EntryPoint = "lancedb_vector_query_limit")]
    internal static partial LanceDbError VectorQueryLimit(VectorQueryHandle q, nuint limit, out nint errorMessage);

    // LanceDBError lancedb_query_result_to_arrow(LanceDBQueryResult*, FFI_ArrowArray***, FFI_ArrowSchema**, size_t*, char**);
    [LibraryImport(Lib, EntryPoint = "lancedb_query_result_to_arrow")]
    internal static partial LanceDbError ResultToArrow(nint result, out nint arrays, out nint schema, out nuint count, out nint errorMessage);

    // void lancedb_free_string(char* str);
    [LibraryImport(Lib, EntryPoint = "lancedb_free_string")]
    internal static partial void FreeString(nint str);
}
```

Opaque handles as `SafeHandle` (the source generator marshals a returned `SafeHandle` subclass that has a
public parameterless ctor):

```csharp
internal sealed class TableHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true) {
    protected override bool ReleaseHandle() { Native.TableFree(handle); return true; }  // void lancedb_table_free(…)
}
```

Error out-param → typed exception:

```csharp
internal enum LanceDbError { Success = 0, InvalidArgument = 1, /* … */ TableNotFound = 4, /* … */ Unknown = 22 }

static void Check(LanceDbError err, nint errorMessage) {
    if (err == LanceDbError.Success) return;
    var detail = errorMessage != 0 ? Marshal.PtrToStringUTF8(errorMessage) : null;
    if (errorMessage != 0) Native.FreeString(errorMessage);   // ownership: we free the message
    throw new LanceDbException(err, detail);
}
```

Callbacks (only `lancedb_run_on_stack` today) use `[UnmanagedCallersOnly]` static methods +
`delegate* unmanaged<…>` function pointers — never marshalled delegates — to stay AOT-safe.

### Arrow C Data Interface bridge

We depend only on the Arrow **C ABI structs** and `liblancedb` — **not** the Arrow **C++** runtime (that
is only needed to build lancedb-c's own C++ examples/tests; see VERIFY #1). On the managed side,
`Apache.Arrow`'s C Data Interface moves data across the boundary:

- **Ingest:** map `TRecord[]` → a `RecordBatch` (source-generated mapper, below) → export into an Arrow
  `CArrowArray` + `CArrowSchema` → pass their pointers to `lancedb_record_batch_reader_from_arrow`.
- **Results:** `lancedb_query_result_to_arrow` yields `FFI_ArrowArray**` + one `FFI_ArrowSchema`; import
  each pointer into a managed `RecordBatch`, project columns back to `TRecord`, read the `_distance`
  column into `VectorSearchResult.Score`.

```csharp
using Apache.Arrow;
using Apache.Arrow.C;

// Ingest: export a managed RecordBatch into the Arrow C ABI structs lancedb-c consumes.
static unsafe nint ReaderFromBatch(RecordBatch batch) {
    CArrowSchema* cSchema = CArrowSchema.Create();
    CArrowArray*  cArray  = CArrowArray.Create();
    try {
        CArrowSchemaExporter.ExportSchema(batch.Schema, cSchema);
        CArrowArrayExporter.ExportRecordBatch(batch, cArray);        // zero-copy: shares buffers
        Check(Native.ReaderFromArrow((nint)cArray, (nint)cSchema, out var reader, out var err), err);
        return reader;   // lancedb consumes the array; merge_insert/add then own the reader
    } finally { CArrowArray.Free(cArray); CArrowSchema.Free(cSchema); }
}

// Results: import each FFI_ArrowArray* the query produced into a managed RecordBatch.
static unsafe RecordBatch ImportBatch(nint arrayPtr, Schema schema) =>
    CArrowArrayImporter.ImportRecordBatch((CArrowArray*)arrayPtr, schema);
```

**Record ↔ RecordBatch mapping is source-generated, not reflected.** A Roslyn generator emits, per
`[VectorStoreKey]`/`[VectorStoreData]`/`[VectorStoreVector]` record, the Arrow schema and the
column-builder / column-reader code — the AOT-clean alternative to reflection, consistent with this
codebase's conventions.

### Secure workspace filtering — the centerpiece

MEVD hands the connector `options.Filter` as `Expression<Func<TRecord,bool>>`
(e.g. `r => r.WorkspaceId == tenant`). We translate it by **tree-walking the expression into
`lancedb_expr_*` calls** — no `Expression.Compile()` (AOT-illegal, IL3050), and no SQL-string assembly
(so no predicate-injection surface):

```csharp
// returns an owned ExprHandle; freed by df_filter consuming it, or by us on error
ExprHandle Visit(Expression node) => node switch {
    BinaryExpression { NodeType: ExpressionType.AndAlso } b => Native.ExprAnd(Visit(b.Left), Visit(b.Right)),
    BinaryExpression { NodeType: ExpressionType.OrElse  } b => Native.ExprOr (Visit(b.Left), Visit(b.Right)),
    BinaryExpression b when Comparison(b.NodeType)          => Native.ExprBinary(Visit(b.Left), MapOp(b.NodeType), Visit(b.Right)),
    MemberExpression m when IsRecordColumn(m)               => Native.ExprColumn(StorageColumn(m)),   // gen'd name map
    ConstantExpression c                                    => Literal(c.Value),                       // _literal_string/_i64/_f64/_bool
    MethodCallExpression mc when IsEnumerableContains(mc)   => InList(mc),                              // _in_list
    _ => throw new NotSupportedException($"Unsupported filter node: {node.NodeType}")
};
```

Tenant scoping is then `lancedb_vector_query_df_filter(q, Visit(filter))`, **pre-filtered by default** —
two workspaces with identical embeddings never cross because the other tenant's rows are excluded before
the vector comparison. A scalar `BTREE`/`BITMAP` index on `workspace_id` keeps the pre-filter cheap.

Security framing (corrected, do not regress to "zero risk"): a shared table + filter still **fails open**
at the app layer — a code path that omits the predicate leaks the table. Mitigations: (a) inject the
tenant predicate **centrally** from the *validated JWT* (never a client header, never per-query
hand-rolled); (b) offer **table-per-tenant** (a table is cheap in LanceDB) for hard, **fail-safe**
isolation. Recommend table-per-tenant for untrusted multi-tenancy, `WHERE`/expr for intra-tenant scoping.

### csproj & native packaging

Connector library (`net10.0`, AOT-clean library):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>   <!-- function pointers / Arrow interop -->
    <IsAotCompatible>true</IsAotCompatible>       <!-- turns on the trim + AOT analyzers (IL2026/IL3050) -->
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.VectorData.Abstractions" Version="10.7.0" />
    <PackageReference Include="Apache.Arrow" Version="23.0.0" /> <!-- C Data Interface: AOT-clean, pure C ABI (verified, VERIFY #2) -->
  </ItemGroup>
  <!-- liblancedb built per-RID by CI (cargo build --release) and dropped here -->
  <ItemGroup>
    <None Include="runtimes/linux-x64/native/liblancedb.so"    Pack="true" PackagePath="runtimes/linux-x64/native/"    CopyToOutputDirectory="PreserveNewest" />
    <None Include="runtimes/linux-arm64/native/liblancedb.so"  Pack="true" PackagePath="runtimes/linux-arm64/native/"  CopyToOutputDirectory="PreserveNewest" />
    <None Include="runtimes/win-x64/native/lancedb.dll"        Pack="true" PackagePath="runtimes/win-x64/native/"      CopyToOutputDirectory="PreserveNewest" />
    <None Include="runtimes/osx-arm64/native/liblancedb.dylib" Pack="true" PackagePath="runtimes/osx-arm64/native/"    CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

Consumer app (Native AOT):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <PublishAot>true</PublishAot>
    <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../LanceDB.Extensions.VectorData/LanceDB.Extensions.VectorData.csproj" />
  </ItemGroup>
</Project>
```

Notes: the `LibraryImport` source generator ships in the .NET SDK (net7+) — no extra package. `crate-type`
also includes `staticlib`, so a single-artifact static-link variant is possible later if per-RID cdylib
distribution proves painful. DI shape mirrors the shipped connectors: an
`services.AddLanceDbVectorStore(uri)` extension registering a `VectorStore`. **.NET 10 native-search note:**
the executable/app directory is no longer implicitly probed for single-file/AOT apps — it's covered by
`DllImportSearchPath.AssemblyDirectory` (in the default set), so keep that default or register a
`NativeLibrary.SetDllImportResolver` fallback. A pure-blittable FFI surface may also add
`[assembly: DisableRuntimeMarshalling]` (assembly-wide; compatible with the Arrow C ABI structs).

### VERIFY items — resolved (from source)

1. **Arrow C++ runtime dependency?** **No.** The C API exchanges data purely via Arrow C ABI structs
   (`FFI_ArrowArray`/`FFI_ArrowSchema`). The .NET consumer needs only `liblancedb` + managed
   `Apache.Arrow`. (`lancedb-c`'s C++20/`pkg-config arrow` requirement is for its own examples/tests.)
2. **`Apache.Arrow` C Data Interface AOT-clean?** **Yes — confirmed from `apache/arrow-dotnet` source.**
   `Apache.Arrow.csproj` sets `IsAotCompatible` on its `net8.0` target (the asset a net10/11 app resolves);
   `CArrowArray`/`CArrowSchema`/`CArrowArrayStream` are `[StructLayout(Sequential)]` mirrors of Arrow's
   `abi.h`; release/stream callbacks use `[UnmanagedCallersOnly]` + `delegate* unmanaged<…>` on net5+ (the
   legacy marshalled-delegate `NativeDelegate.cs` path is compiled out); no reflection / `Requires*` / DAM
   in the `C/` sources. Pin **`Apache.Arrow` 23.0.0**. It is pure C ABI — no Arrow C++ runtime.
3. **Async model?** **Blocking.** All operations are synchronous C calls (tokio `block_on` internally;
   `lancedb_run_on_stack` grows the stack for the embedded runtime). Implication: MEVD's async methods
   wrap **blocking** native calls — we must decide an offload policy (dedicated thread / `Task.Run`) so
   we don't stall async worker threads; do not pretend they're truly async.
4. **`panic = "abort"`?** Not set in `Cargo.toml`. Relies on Rust's default `extern "C"` unwind→abort
   (safe since Rust 1.81, and the header forbids unwinding across the boundary). Confirm on the pinned
   commit; consider requesting an explicit abort profile upstream.

## Alternatives Considered

- **Community `lennylxx/lancedb-csharp` SDK.** Ships prebuilt natives and a `.Where(...)` API. Rejected as
  the base per the decision to own the AOT interop over the official C header; its AOT-cleanliness is
  unverified and it is not an MEVD connector. Useful as a reference for P/Invoke shapes.
- **SQL-string filtering** (`lancedb_query_where_filter`). Rejected as the primary path in favour of the
  **DataFusion expression builder** — structured translation from LINQ, no string-injection surface.
- **FAISS** (own bindings / FaissNet). Rejected — see the
  [superseded doc](../2026-07-08-faiss-vectordata-connector-aot/design.md): no filtered search in FaissNet,
  batch-size-1 + no-GPU selector upstream, mandatory separate metadata store.

## Open Questions

- **Async offload policy** for the blocking C calls (thread-pool hop vs a dedicated DB thread/queue).
- **Score semantics:** LanceDB returns `_distance`; confirm the orientation to surface as
  `VectorSearchResult.Score` per metric (L2 vs cosine vs dot).
- **RID matrix + CI** to `cargo build` `liblancedb` (linux-x64/arm64, win-x64, osx-arm64; osx-x64 TBD);
  pin `lancedb-c` (currently `v0.30.0`, pre-1.0 — expect churn).
- **Repo home / namespace:** standalone library vs under `src/` tied to the Kontext/Engram workstream.

## Phased plan (the connector is the goal throughout)

- **Phase 1 — interop spike.** AOT-published round-trip: `connect → create table → merge_insert (Arrow) →
  vector_query + df_filter("workspace_id") → result_to_arrow`, zero IL2026/IL3050. Proves the Arrow bridge
  end-to-end + native packaging (Apache.Arrow AOT-cleanliness already confirmed from source, VERIFY #2).
  *(Not the deliverable — the first checkpoint of it.)*
- **Phase 2 — the connector (indexing included).** `LanceDbVectorStore` + `LanceDbCollection<TKey,TRecord>`
  implementing the full MEVD surface (Ensure/Get/Upsert/Delete/Search), the source-generated
  record↔RecordBatch mapper, the LINQ→`lancedb_expr_*` filter translator, **and index creation from the
  record definition** — `EnsureCollectionExistsAsync` builds the scalar index on filter columns and the
  vector index on the embedding (kind + distance as the record declares), plus wiring the ANN search
  tuners. **This is the deliverable.**
- **Phase 3 — packaging & hardening.** Per-RID native build/CI, async-offload policy, table-per-tenant
  option, ANN tuning *defaults* + filtered-recall validation, NuGet packaging. *(Ops/optimization only —
  no capability is deferred here.)*

## References

- [`lancedb/lancedb-c`](https://github.com/lancedb/lancedb-c) — official C bindings (read @ v0.30.0: `include/lancedb.h`, `Cargo.toml`, `examples/`)
- [LanceDB metadata filtering (pre-filter default)](https://docs.lancedb.com/search/filtering) · [`lancedb` Rust crate](https://docs.rs/lancedb/latest/lancedb/)
- [Build your own MEVD connector](https://learn.microsoft.com/semantic-kernel/concepts/vector-store-connectors/how-to/build-your-own-connector) · [`VectorStoreCollection<TKey,TRecord>`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.vectordata.vectorstorecollection-2)
- [Arrow C Data Interface](https://arrow.apache.org/docs/format/CDataInterface.html) · [`Apache.Arrow` C# — `apache/arrow-dotnet`, `src/Apache.Arrow/C/`](https://github.com/apache/arrow-dotnet) · [NuGet `Apache.Arrow` 23.0.0](https://www.nuget.org/packages/Apache.Arrow)
- [`LibraryImport` source-generated P/Invoke](https://learn.microsoft.com/dotnet/standard/native-interop/pinvoke-source-generation) · [`[UnmanagedCallersOnly]`](https://learn.microsoft.com/dotnet/api/system.runtime.interopservices.unmanagedcallersonlyattribute) · [IL3050 / RequiresDynamicCode](https://learn.microsoft.com/dotnet/core/deploying/native-aot/warnings/il3050) · [Native libs in NuGet packages](https://learn.microsoft.com/nuget/create-packages/native-files-in-net-packages) · [.NET 10 native library search change](https://learn.microsoft.com/dotnet/core/compatibility/interop/10.0/native-library-search)
