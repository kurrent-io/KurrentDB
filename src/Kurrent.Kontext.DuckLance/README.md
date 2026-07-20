# DuckLance

A [`Microsoft.Extensions.VectorData`](https://learn.microsoft.com/en-us/semantic-kernel/concepts/vector-store-connectors/) (MEVD)
vector store connector backed by [DuckDB](https://duckdb.org/) and its `lance` core extension
([Lance](https://lancedb.github.io/lance/) storage format).

This is a **private Kurrent package**: `Kurrent.SemanticKernel.Connectors.DuckLance`. It is consumed via a local NuGet
feed and is **never published** to nuget.org or any public feed. It lives in this fork of Semantic Kernel alongside the
in-tree MEVD connectors it was modeled on, but its source is fully self-contained (the two formerly-shared utility files
are vendored under `Internal/` — see `PORTING.md`) and it ships and versions independently of the rest of
`Microsoft.SemanticKernel.*`: the two project folders can be copied to another repository wholesale, needing only the
NuGet pins and MSBuild settings documented in `PORTING.md` Section B.

The authoritative design spec, implementation plan, and validation notes live in the `kurrentdb` repo:

- `.claude/context/docs/designs/duckdb-connector-spec.md` — full technical design spec
- `.claude/context/docs/designs/ducklance-implementation-plan.md` — staged build plan
- `.claude/context/docs/designs/ducklance-validation-report.md` — empirical validation against the `lance` extension
- `.claude/context/docs/designs/duckdb-lancedb-reference.md` — DuckDB/Lance reference notes

## Feature table

In the same format Microsoft's own connector docs use:

| Feature Area                          | Support                                                                                                                                                          |
|---------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| Collection maps to                    | A table in one shared attached Lance namespace per store                                                                                                         |
| Supported key property types          | `string` only                                                                                                                                                    |
| Supported vector property types       | `ReadOnlyMemory<float>`, `Embedding<float>`, `float[]`                                                                                                           |
| Supported index types                 | `IvfFlat`, `QuantizedFlat`, `Hnsw`, `Dynamic` (defaults to quantized `IVF_PQ`). `DiskAnn` **not supported** (throws `NotSupportedException`)                     |
| Supported distance functions          | `CosineDistance`, `CosineSimilarity`, `DotProductSimilarity`, `EuclideanSquaredDistance`                                                                         |
| Supported filter clauses              | Property equality (`r => r.Prop == value`) and list/tag containment (`r => r.Tags.Contains(value)`) only — expressed as LINQ, not the obsolete fluent filter API |
| Supports multiple vectors in a record | Yes                                                                                                                                                              |
| IsIndexed supported?                  | Yes                                                                                                                                                              |
| IsFullTextIndexed supported?          | Yes (drives hybrid search's text-property selection)                                                                                                             |
| StorageName supported?                | Yes                                                                                                                                                              |
| HybridSearch supported?               | Yes                                                                                                                                                              |

## Quick start

```csharp
using Kurrent.SemanticKernel.Connectors.DuckLance;
using Microsoft.Extensions.VectorData;

var options = new DuckDBVectorStoreOptions
{
    DatabasePath = "/var/data/kontext/duck.db",
};

using var store = new DuckDBVectorStore(options);
using var collection = store.GetCollection<string, Document>("docs");
await collection.EnsureCollectionExistsAsync();

await collection.UpsertAsync(new Document
{
    Id = "1",
    Text = "Semantic Kernel is a model-agnostic SDK.",
    Vector = new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f, 0.4f]),
});

await foreach (var result in collection.SearchAsync(new ReadOnlyMemory<float>([0.1f, 0.2f, 0.3f, 0.4f]), top: 5))
{
    Console.WriteLine($"{result.Record.Id}: {result.Score}");
}

sealed class Document
{
    [VectorStoreKey]
    public string Id { get; set; } = string.Empty;

    [VectorStoreData]
    public string Text { get; set; } = string.Empty;

    [VectorStoreVector(4)]
    public ReadOnlyMemory<float> Vector { get; set; }
}
```

## DI registration

`DuckLanceServiceCollectionExtensions` registers the store only — not individual collections, since
`DuckDBCollection<TKey, TRecord>` has an internal constructor and is only obtainable by calling
`VectorStore.GetCollection<TKey, TRecord>()`/`GetDynamicCollection()` on a resolved store.

```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddDuckLanceVectorStore(new DuckDBVectorStoreOptions
{
    DatabasePath = "/var/data/kontext/duck.db",
});

// Resolves the same singleton instance for both service types:
var store = provider.GetRequiredService<DuckDBVectorStore>();
var vectorStore = provider.GetRequiredService<VectorStore>();
```

`AddDuckLanceVectorStore`/`AddKeyedDuckLanceVectorStore` are each available with an eager `DuckDBVectorStoreOptions`
overload and a `Func<IServiceProvider, DuckDBVectorStoreOptions>` overload (invoked lazily against the built
`IServiceProvider`), both defaulting to `ServiceLifetime.Singleton`. If the resolved options don't carry an
`EmbeddingGenerator`, registration falls back to any `IEmbeddingGenerator` registered on the `IServiceProvider`,
applying it to a defensive copy of the options so the caller's original instance is never mutated.

## Platform support

The DuckDB `lance` extension only ships binaries for a subset of the platforms DuckDB itself supports:

| OS      | Architecture          | Supported |
|---------|-----------------------|-----------|
| Linux   | x64                   | Yes       |
| Linux   | arm64                 | Yes       |
| macOS   | arm64 (Apple Silicon) | Yes       |
| macOS   | x64 (Intel)           | **No**    |
| Windows | x64                   | Yes       |

## Operational notes

- **Storage layout.** Everything derives by convention from `DatabasePath` — the DuckDB database file, whose path and
  name the caller chooses freely (`duck.db`, `/dev/donald.duck`, …). The file's directory is attached as the Lance
  namespace (attachment alias `ldb`), and every collection is a table named after the collection itself, backed by a
  `{collection}.lance` dataset in that directory. The database file holds no vector data — all durable vector state
  lives in the `.lance` datasets — but it is itself durable: created on first use, `CHECKPOINT`ed when the store is
  disposed, and **never deleted** by the connector, so a later store (or, once this process releases its lock, a new
  process) reopens the very same file. One constraint is enforced at construction: the file name's stem (up to the
  first `.`) must not equal the `ldb` alias — DuckDB names the database's own catalog after the stem, and a collision
  would make the Lance attach silently no-op.
- **Single-process ownership, cross-process reads.** The database file is opened `READ_WRITE`, so DuckDB takes an
  exclusive OS lock on it: only ONE process may open a given `DatabasePath` at a time. A second process opening the
  same file fails fast, verbatim:

  > `IO Error: Could not set lock on file "…/duck.db": Conflicting lock is held in … (PID …) by user …. See also https://duckdb.org/docs/stable/connect/concurrency`

  Within a single process, any number of stores over the same `DatabasePath` share one DuckDB engine instance
  (DuckDB.NET caches a single native instance per database-file path) and cooperate safely. The Lance data itself
  remains readable from another process through a read-only DuckDB connection or separate Lance tooling pointed at
  the `.lance` datasets — only the DuckDB database file is single-writer.
- **Memory budget.** `DuckDBVectorStoreOptions.MemoryLimitMib` caps the DuckDB engine's memory budget (mapped to DuckDB's
  `memory_limit` setting, in MiB). Leave it `null` (the default) to use DuckDB's own default budget.
- **Background reindexing.** Every dataset (one per collection) gets an always-on background reindex scheduler, created
  lazily on first `GetCollection`/`GetDynamicCollection` and disposed with the store. It periodically append-optimizes
  indexes once the unindexed-row ratio/floor is crossed (`DuckDBReindexOptions.UnindexedRatioThreshold`/`UnindexedRowFloor`),
  retrains on a time cadence (`RetrainInterval`), and runs `VACUUM LANCE` on its own schedule. Call
  `DuckDBCollection<TKey, TRecord>.OptimizeIndexesAsync()` to trigger the same append/compact logic on demand instead of
  waiting for the next tick.
- **Lazy vector indexes.** Vector indexes are only trained once a collection's table holds at least **256 rows** — Lance's
  own index training fails below that floor for every index type. Below the floor, searches fall back to Lance's
  brute-force scan, so results are always correct, just not index-accelerated, until the floor is crossed.
- **Vacuum retention is conservative by default.** `DuckDBReindexOptions.VacuumRetention` defaults to 14 days (with
  `VacuumRetainVersions = 3`). Do not shorten this without care: an aggressive `VACUUM LANCE` retention window can break
  cached dataset handles held by still-open connections, so the retention window must stay comfortably longer than any
  plausible connection lifetime.

## Known behaviors/limitations

- **Storage names are always lowercased.** The `lance` extension (as of build `533e0ee`) has broken `DELETE` predicate
  pushdown for columns whose names contain uppercase letters — `DELETE ... WHERE Id = ?` silently matches zero rows even
  though the identical `SELECT` matches the row, so the row is never removed. `INSERT`/`MERGE`/`SELECT` are unaffected.
  To avoid this trap unconditionally, every property's storage name is lowercased at model-build time, and two properties
  that collide only by case (e.g. `Id`/`ID`) are rejected up front with an `ArgumentException`.
- **Reserved-word storage names are rejected.** Property storage names (already lowercased, already charset-validated) that
  collide with DuckDB reserved words (matched case-insensitively against `duckdb_keywords()` with category `'reserved'`) are
  rejected at `EnsureCollectionExistsAsync` time with a `VectorStoreException`. Reason: the lance extension's DELETE predicate
  pushdown silently matches zero rows for a quoted reserved-word column, even though unquoted reserved words in a CREATE TABLE
  break the table creation itself. A reserved-word property therefore cannot be used correctly and is rejected up front.
- **Identifier charset.** Store keys, collection names, and the table names resolved from them must match
  `^[A-Za-z0-9_]+$` (letters, digits, underscores only — no hyphens or periods, unlike Lance's own, slightly looser rule),
  because the resolved name is interpolated unquoted into SQL and into `'alias.main.table'`-style search URIs. Column
  storage names must match `^[A-Za-z_][A-Za-z0-9_]*$` (after the lowercasing above).
- **No arbitrary `OrderBy` in search.** `VectorSearchOptions<TRecord>`/`HybridSearchOptions<TRecord>` only rank by
  relevance score; there is no way to request "filtered hybrid search, ordered by an arbitrary field" in one call.
  Request a larger `Top` candidate pool and sort/take client-side if needed — this has an inherent recall tradeoff.
- **Tag-containment filters oversample.** `r => r.Tags.Contains(value)` is not prefilterable through the `lance` extension
  today (its `WHERE` → Lance filter translator has no `array_has_any`/`list_contains` translation), so it is evaluated as a
  post-filter: the connector requests an oversampled `k` from `lance_vector_search`, evaluates the containment predicate
  itself, then applies `LIMIT top`. This carries the same recall-bound caveat as the `OrderBy` workaround above — a
  genuinely matching row that falls outside the oversampled candidate pool won't appear. Plain equality filters, by
  contrast, are a true prefilter and do not oversample.
- **`ScoreThreshold` is supported, applied client-side.** `VectorSearchOptions<TRecord>.ScoreThreshold` and
  `HybridSearchOptions<TRecord>.ScoreThreshold` filter results after the top-k rows are fetched and their scores are
  converted. For similarity functions (`CosineSimilarity`, `DotProductSimilarity`), a result passes the threshold when its
  score is `>= threshold`; for distance functions (`CosineDistance`, `EuclideanSquaredDistance`, and the null default), it
  passes when score is `<= threshold`. The returned result count may be fewer than `Top` when some rows within the top-k
  fall below/above the threshold, consistent with the MEVD convention.
- **`byte[]` (BLOB) properties are fully supported.** Round-trip survives through `byte[]` ↔ DuckDB BLOB ↔ `byte[]`.
  DuckDB.NET's ADO.NET reader returns BLOBs as a `Stream` (specifically `UnmanagedMemoryStream`); the connector coerces
  the stream to `byte[]` by reading it fully and disposing.
- **Date/time round-trip semantics.** `DateTime` round-trips by ticks with `Kind` coming back as `Unspecified`;
  `DateTimeOffset` round-trips the UTC instant (offset normalized to zero), preserving the absolute clock reading.
- **Batch upsert uses multi-row MERGE with chunking and deduplication.** `UpsertAsync` batches records into multi-row
  MERGE statements (up to 500 rows per statement) to achieve one Lance commit per chunk instead of one per record. Duplicate
  keys within a batch are deduplicated client-side with last-wins semantics before the MERGE, preventing the underlying
  multi-row MERGE (which evaluates all source rows against the pre-merge target) from double-inserting same-key duplicates.
- **A BTREE index is automatically created on the key column.** Every `EnsureCollectionExistsAsync` call creates a
  `CREATE INDEX {key}_idx ON '{dataset_path}' ({key}) USING BTREE` to accelerate upserts (which join on the key via MERGE)
  and key-based Get/Delete operations. This is a major performance factor for large batch operations and hot-key contention.
- **Concurrent writers: Lance MVCC with transient-conflict retry and connection recycling.** The connector automatically
  retries operations up to three attempts (two retries with backoff) when Lance rejects a commit as transient optimistic-concurrency
  conflict ("Retryable commit conflict"). For stale-dataset-handle errors (dataset rewrite or concurrent-writer fragment view),
  the poisoned connection is recycled — discarded and kept out of the pool — and the operation retries once on a freshly opened
  connection (which gets its own three-attempt retry budget). Under sustained same-row contention from concurrent writers
  (multiple stores in a process, or many threads through one store), exhaustion of retry budgets can surface as
  `VectorStoreException` — a documented Lance failure mode; hot-row writers should retry at the call site or serialize their
  writes.

## Building/packing

This package is built and packed like any other project, but only ever published to a local feed directory, never to
nuget.org:

```bash
dotnet pack dotnet/src/VectorData/DuckLance/DuckLance.csproj -c Release -o <feed-dir>
```

Add `<feed-dir>` as a local NuGet source (e.g. via `nuget.config` or `dotnet nuget add source`) in whatever project
consumes `Kurrent.SemanticKernel.Connectors.DuckLance`.
