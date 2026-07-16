---
title: "DuckDB + LanceDB for Kontext Hybrid Search — Can DuckDB Read/Write Lance In Place?"
type: analysis
date: 2026-07-08
author: sergio
tags: [kontext, duckdb, lancedb, lance, hybrid-search, vector-search, fts, dotnet]
scope: >
  Feasibility of replacing Kontext's Lucene (FTS) + USearch (vector) stack with a single
  DuckDB + Lance stack using the DuckDB `lance` core extension; zero-copy/in-place access,
  full read+write path, vector-search viability vs DuckDB VSS and USearch, FTS vs Lucene,
  hybrid fusion, and .NET binding realities.
related: [2026-07-06-engram-memory-design-review, 2026-07-03-agentic-memory-systems]
---

## Summary

The premise of the question has been overtaken by events: as of **DuckDB 1.5.1 (23 Mar 2026)** and the **LTS 1.4.4 / 1.5.0** lines, DuckDB ships an **official `lance` *core* extension** (built by DuckLabs + LanceDB, repo `lance-format/lance-duckdb`, latest **v0.5.4**, 8 Apr 2026) that **reads *and* writes the Lance columnar format in place** — there is no need to "copy data out of DuckDB into a separate store." You `INSTALL lance; LOAD lance;` and query `.lance` datasets directly with three built-in table functions: `lance_vector_search` (ANN → `_distance`), `lance_fts` (BM25 → `_score`), and `lance_hybrid_search` (fused → `_hybrid_score`). The write path is **not read-only**: beyond bulk `COPY … TO (FORMAT lance)` and CTAS, attaching a Lance namespace (`ATTACH … (TYPE lance)`) enables full row-level `INSERT`/`UPDATE`/`DELETE`/`MERGE INTO` (upsert)/`TRUNCATE`/`ALTER TABLE`, plus vector/scalar/full-text index DDL and maintenance (`OPTIMIZE`). This means a **single DuckDB + Lance stack can, in principle, cover both the FTS half (BM25, the same model Lucene uses) and the vector half in one SQL surface** — replacing both Lucene and USearch conceptually.

The two big caveats that must gate any decision: (1) the extension is **pre-1.0 (v0.5.x)** and its documented vector index is **IVF_FLAT only** — **IVF_PQ and HNSW are not documented** in the extension's SQL surface, and fusion is **alpha-weighted linear scoring, not reciprocal rank fusion**; (2) there is **no official .NET/C# binding for Lance** (official SDKs are Python, Rust, Java/JNI), so a C#/.NET Kontext service would drive everything through **DuckDB.NET** (which does ship native binaries for linux x64/arm64, osx arm64, win x64) executing SQL against the loaded `lance` extension. Note also that DuckDB's *own* native vector extension, **VSS, is explicitly experimental and not recommended for production persistence** (WAL recovery for custom indexes is unimplemented; persistent HNSW is gated behind `hnsw_enable_experimental_persistence` with a documented data-loss/corruption risk), and VSS is itself **built on the USearch library** — so "DuckDB VSS" is not a distinct-from-USearch option so much as USearch embedded. The Lance route is the more credible path for Kontext, but it should be **prototyped and benchmarked**, not adopted on documentation alone: no independent recall/latency benchmarks for the hybrid path were found (the one DuckDB blog figure is ~17 ms for a hybrid query; a community Python PoC reported ~14 s dominated by an embedding API call).

## Findings

### 1. Zero-copy / in-place access — **YES, no second copy required** (confidence: high)

- The `lance` extension exposes **table-valued functions that read a `.lance` dataset path directly** — e.g. `SELECT id, label, _distance FROM lance_vector_search('path/to/dataset.lance', 'vec', [...]::FLOAT[4], k=5, prefilter=true) ORDER BY _distance ASC` — so DuckDB queries Lance data in place rather than materializing a second copy (`lance-format/lance-duckdb` `docs/sql.md`; `duckdb.org/docs/current/core_extensions/lance`). (high)
- The v0.2.0 release added **filter and projection pushdown** into the Lance scan, plus S3 auth via DuckDB Secrets and `EXPLAIN (FORMAT JSON)` (`github.com/lance-format/lance-duckdb/releases`; corroborated by MotherDuck's April 2026 newsletter). (high)
- **Important mechanism nuance:** the ANN/BM25/fusion work executes **inside the embedded Lance Rust library via FFI**, not in DuckDB's own execution engine; results stream back into DuckDB. So "zero-copy" means *no separate materialized store and no ETL round-trip* — data lives once, in Lance format on disk, queried in place. It does **not** mean DuckDB's vectorized engine runs the ANN itself. (high)

### 2. Full read AND write path — **NOT read-only; full DML + index maintenance** (confidence: high)

- The extension's tagline is literally "enable reading and writing of lance tables." Write paths: `COPY (SELECT …) TO 'out.lance' (FORMAT lance, mode 'overwrite'|'append')`, `write_empty_file true` for schema-only datasets, and `CREATE TABLE AS SELECT` (`github.com/lance-format/lance-duckdb`; `duckdb.org/docs/lts/core_extensions/lance`). (high)
- **Row-level DML** is documented on attached namespaces (`ATTACH 'dir' AS ns (TYPE lance)`): `INSERT` (VALUES and SELECT), `UPDATE … WHERE`, `DELETE … WHERE`, `MERGE INTO … WHEN MATCHED THEN UPDATE/DELETE … WHEN NOT MATCHED THEN INSERT` (a real **upsert**), `TRUNCATE TABLE`, and `ALTER TABLE` ADD/RENAME/ALTER-TYPE/DROP COLUMN (schema evolution) (`docs/sql.md`; DuckDB blog *Test-Driving the Lance Lakehouse Format*, 2026-05-21). (high)
- **Index DDL + maintenance:** `CREATE INDEX … USING IVF_FLAT WITH (num_partitions, metric_type)` (vector), `USING BTREE` (scalar), `USING INVERTED` (full-text); plus `SHOW INDEXES`, `DROP INDEX`, and `ALTER INDEX … OPTIMIZE WITH (mode='merge'|'append'|'retrain')` (`docs/sql.md`; DuckDB 2026-05-21 blog). Transactions honor `BEGIN`/`COMMIT`/`ROLLBACK` for `DELETE` (v0.4.0) with MVCC/ACID-style semantics. (high)
- **Delivery timeline** (release notes, `github.com/lance-format/lance-duckdb/releases`): v0.3.0 (2025-12-28) COPY writer + DDL + DML + index DDL; v0.4.0 (2025-12-29) transactional DELETE + UPDATE-all-rows; v0.4.1 (2026-01) `nprobs`/`refine_factor` params; v0.5.3 (2026-03-24) full maintenance SQL surface + `MERGE INTO`; v0.5.4 (2026-04-08) vector-index controls in hybrid search. (high)
- **Adversarial note:** several extracted claims asserting "writes are COPY-only / row mutation unsupported / no index maintenance" were **refuted** during verification — they came from reading only the terse LTS docs page, which defers DML to the linked SQL reference. Do **not** rely on that page in isolation. (high — refutation)
- **Documented gap:** `ALTER COLUMN … SET NOT NULL` is not yet supported; `CREATE INDEX` is single-column; vector columns must be fixed-size `FLOAT[N]` (cast `FLOAT[]`→`FLOAT[N]` first). (high)

### 3. Vector-search viability, and vs DuckDB VSS / USearch (confidence: high on capability, low on performance)

- **Lance ANN via the extension:** `lance_vector_search` supports `k` (default 10), `use_index` (default true), `nprobs` (IVF partitions to probe), `refine_factor` (over-fetch for re-ranking with original vectors), and `prefilter` (default false — "filters applied before top-k selection", i.e. **metadata prefiltering**). Returns `_distance` (smaller = closer) (`docs/sql.md`; DeepWiki search-functions page). (high)
- **Only IVF_FLAT is documented** as the vector index type in the extension's SQL surface — **`IVF_PQ` and `HNSW` do not appear** anywhere in `docs/sql.md` or the release notes (verified by grep). The Lance *format* supports richer indexes, and HNSW in LanceDB is a sub-index inside IVF partitions, but that is **not surfaced** by the current extension. This is the single biggest capability caveat for a vector-heavy workload. (high)
- **DuckDB's native VSS extension is a weak alternative for Kontext:** its docs open with "The `vss` extension is an **experimental** extension"; persistent HNSW requires `SET hnsw_enable_experimental_persistence = true`, and "WAL recovery is not yet properly implemented for custom indexes … you can end up with **data loss or corruption of the index**", with DuckDB explicitly recommending "**do not use this feature in production environments**" (`duckdb.org/docs/current/core_extensions/vss`, verified July 2026; `github.com/duckdb/duckdb-vss`). VSS supports HNSW with metrics l2sq/cosine/inner_product; deletes are lazy (marked, not removed) and require `PRAGMA hnsw_compact_index` or rebuild. (high)
- **VSS is built on USearch:** the VSS docs state it "is based on the usearch library." So "switch to DuckDB VSS" is effectively "run USearch embedded inside DuckDB, experimentally" — not a distinct, more-mature engine. If the goal is to *retire* USearch for a production-grade store, VSS does not achieve it; the **Lance** route does. (high)

### 4. Extension maturity & status (confidence: high)

- `lance` is a **core** DuckDB extension (installed with bare `INSTALL lance;`, no `FROM community`), listed among DuckDB's ~29–31 core extensions alongside `vss`, `fts`, `iceberg`, `delta`; it is **absent from the community-extensions registry**, confirming core status (`duckdb.org/docs/current/core_extensions/overview`; LanceDB announcement). (high)
- Maintained in a third-party repo (`lance-format/lance-duckdb`, Apache-2.0, C++), which is normal for DuckDB core extensions (spatial/delta/iceberg do the same). Actively maintained (last push 2026-06-10; ~10 releases across 2026; Lance dependency upgraded to v1.0.0/v3.0.1 lines). (high)
- **Still pre-1.0 (v0.5.4).** Documented behavior is real (the docs even list negative limitations, a sign of implemented-not-aspirational features), but the surface is young and evolving — pushdown/parallelism and index-type coverage are still maturing. (high)

### 5. DuckDB FTS vs Lucene (confidence: high)

- DuckDB's **`fts` core extension** provides full-text search "similar to SQLite's FTS5" and ranks with the **Okapi BM25** model (`match_bm25(input_id, query, fields:=NULL, k:=1.2, b:=0.75, …)`) — **the same relevance model modern Lucene uses by default**, making it a plausible functional substitute for the FTS half (`duckdb.org/docs/current/core_extensions/full_text_search`). (high)
- **Key operational limitation of DuckDB FTS:** "The FTS index will **not update automatically** when the input table changes" — you must rebuild the index. This matters for Kontext's write-heavy memory workload. (high)
- **For the Lance route, FTS is handled by `lance_fts` (BM25-like inverted index) inside Lance itself**, not by DuckDB's `fts` extension — so you would generally *not* mix DuckDB `fts` + Lance. A reference project did split DuckDB-`fts`-for-text + LanceDB-IVF_PQ-for-vectors, but the integrated `lance_hybrid_search` path avoids that split. (high)

### 6. Hybrid search implementation (FTS + vector fusion) (confidence: high on mechanism, medium on quality)

- Hybrid search is a **dedicated single function**: `lance_hybrid_search(uri, vector_column, query_vector, text_column, query, k, prefilter, alpha, oversample_factor)` returning `_hybrid_score`, `_distance`, and `_score` in one SQL call. It fuses BM25 text scoring and ANN vector similarity internally (`docs/sql.md`; `duckdb.org/docs/current/core_extensions/lance`; DuckDB 2026-05-21 blog; LanceDB "Lance × DuckDB" blog). (high)
- **Fusion is alpha-weighted linear score mixing, NOT reciprocal rank fusion.** `alpha` (default 0.5) weights vector vs text (0.0 = pure FTS, 1.0 = pure vector); `oversample_factor` (default 4) controls candidate over-fetch/merge. If Kontext specifically wants RRF, it would need to be implemented on top (fetch `lance_vector_search` + `lance_fts` separately and fuse in SQL/app code). (high)
- **Adversarial note:** claims describing hybrid as "combine `lance_vector_search()` and `lance_fts()`" were **refuted** — hybrid is the purpose-built `lance_hybrid_search`, and the docs say to use it "rather than combining the other two." (high — refutation)
- **Performance is essentially unproven.** The only latency data points found: DuckDB's own blog cites a hybrid query at **~17 ms**; a single-commit community Python PoC reported hybrid **~14 s** (dominated by a synchronous embedding API call, not the search itself), with **no recall/precision benchmarks**. Treat both as anecdotal. (medium/low)

### 7. .NET / DuckDB.NET binding considerations (confidence: medium)

- **There is no official Lance/LanceDB .NET SDK.** Official bindings are **Python (PyLance/PyO3), Rust (core), and Java (JNI)** — no C#/.NET binding is documented in the Lance repo. A C#/.NET service therefore accesses Lance **through DuckDB**: load the `lance` extension in a DuckDB connection and issue SQL via **DuckDB.NET**. (medium)
- **DuckDB.NET** ships prebuilt native binaries for linux_amd64/linux_arm64/osx_arm64/windows_amd64 — covering Kontext's likely deployment targets without a native toolchain. This is the pragmatic integration surface: C# → DuckDB.NET → `INSTALL/LOAD lance` → `lance_hybrid_search(...)`. (medium — inferred from prebuilt-binary evidence)
- An **unofficial community LanceDB .NET binding** exists (created Nov 2024, ~10 stars/2 forks, actively released — v2.5.0 on 2026-07-05, targeting .NET 8.0+/.NET Standard 2.0, linux/win x64 + macOS arm64). It carries **bus-factor/maintenance risk** and is not a substitute for going through DuckDB.NET for the SQL surface. (medium)
- This project targets **.NET 10 and AOT** (per repo conventions). DuckDB.NET is a native-interop ADO.NET-style provider — AOT/trimming compatibility of DuckDB.NET and the community binding is **unverified** and must be checked before committing. (low — not covered by sources; flagged as open)

## Recommendation

**For Kontext: the DuckDB + Lance route is viable and is the strongest single-stack option to replace Lucene + USearch — but adopt it via a benchmarked prototype, not on docs alone.**

- **You do not need to copy data out of DuckDB.** The `lance` core extension reads and writes Lance datasets in place, so a memory record's text + embedding + scalar metadata live once in a Lance dataset and are queried directly. This directly satisfies the "no duplication" goal.
- **Realistic architecture:** store Kontext memories as a Lance dataset; attach it as a namespace for row-level upsert/delete of memory claims (`MERGE INTO` fits the revise/retract model); build an `INVERTED` (FTS) index and an `IVF_FLAT` vector index; serve retrieval with `lance_hybrid_search` from C# via DuckDB.NET. This collapses Lucene + USearch into one store and one query.
- **Do NOT use DuckDB VSS** as the vector engine — it is experimental, not production-recommended for persistence, and is just embedded USearch. If you were going to keep an embedded-USearch-class engine you'd gain little; the point of moving to Lance is a persistent, mutable, hybrid-capable store.
- **Gate the decision on a spike that measures:** (a) recall/latency of `lance_hybrid_search` on Kontext-scale data with IVF_FLAT (and whether IVF_FLAT-only recall/latency is acceptable, given IVF_PQ/HNSW aren't exposed); (b) upsert/delete throughput via attached-namespace DML under the memory write pattern; (c) DuckDB.NET + `lance` extension behavior under .NET 10 / AOT; (d) whether alpha-weighted fusion is good enough or RRF must be added in app code.
- **Migration posture:** this is a **pre-1.0 extension**. Pin exact versions, budget for surface churn, and keep the retrieval layer behind an interface so the fusion/index specifics can change without rippling through Kontext.

## Caveats

- **Fast-moving, pre-1.0 target.** All Lance-extension findings are from Dec 2025–Jun 2026 sources; the surface is evolving (index types, pushdown, parallelism). Re-verify against the current release before implementation.
- **Performance is not established.** No independent, reproducible recall/latency/throughput benchmarks for `lance_hybrid_search` were found. The 17 ms (DuckDB blog) and 14 s (community PoC) figures are anecdotal and not comparable.
- **IVF_PQ / HNSW not exposed** by the extension's documented SQL surface (IVF_FLAT only) — a real constraint for large vector sets where PQ compression or HNSW recall/latency would matter.
- **Fusion ≠ RRF.** The built-in fusion is alpha-weighted linear scoring; the question's "RRF or similar" is only partially met.
- **.NET is second-class.** No official Lance .NET SDK; integration is via DuckDB.NET SQL. AOT/trim compatibility for this project's .NET 10 + AOT constraints is **unverified** in the sourced material.
- **"LanceDB" vs "Lance" terminology.** The DuckDB extension targets the open **Lance columnar format** (`lance-format/lance-duckdb`); "LanceDB" is the managed/embedded product built on that format. For Kontext, the relevant capability is the Lance format via DuckDB — running the LanceDB product as well is a separate choice.
- Some source pages (terse LTS docs, README-only fetches) **understate** the write surface; the authoritative reference is `lance-format/lance-duckdb/docs/sql.md` plus the release notes.

## Open Questions

1. What are `lance_hybrid_search` recall and p50/p99 latency on Kontext-scale data with IVF_FLAT, and is the absence of IVF_PQ/HNSW a blocker at the target corpus size?
2. Does DuckDB.NET + the `lance` core extension work under this project's **.NET 10 + AOT/trimming** constraints, and are native binaries available/loadable in the target runtime?
3. What is the upsert/delete latency and index-staleness behavior of attached-namespace DML under Kontext's memory revise/retract write pattern (how often must indexes be `OPTIMIZE`d/rebuilt)?
4. Is alpha-weighted fusion sufficient for Kontext relevance, or is a separate RRF layer (over `lance_vector_search` + `lance_fts`) required?

## Method

- **Source:** background `deep-research` workflow run (`wf_1222eac2-ee4`), 2026-07-08. Scope decomposed into 6 angles (extension status/zero-copy, write path, vector viability vs alternatives, FTS/hybrid fusion, .NET embedding, Lance format internals); ~53 sources fetched across the angles; **247 claims extracted**; top claims put through **3-vote adversarial verification**. This report is synthesized from the **80 confirmed** and **11 refuted** verdicts recovered from the run's on-disk journal after the workflow's final structured-output synthesis step failed (schema-validation retry cap) and the run was stopped for cost.
- **Primary sources cited:** `duckdb.org/docs/current/core_extensions/lance` and `/lts/`; `github.com/lance-format/lance-duckdb` (`docs/sql.md`, releases); `duckdb.org/2026/03/23/announcing-duckdb-151`; `duckdb.org/2026/05/21/test-driving-lance`; `duckdb.org/docs/current/core_extensions/vss`; `github.com/duckdb/duckdb-vss`; `duckdb.org/docs/current/core_extensions/full_text_search`; LanceDB "Lance × DuckDB: SQL for Retrieval" blog; MotherDuck April 2026 newsletter; DeepWiki `lance-format/lance-duckdb` and `duckdb/duckdb-vss` mirrors; a community LanceDB .NET binding repo (unofficial).
- **Not covered / limits:** no hands-on benchmarking; AOT/.NET-10 compatibility of DuckDB.NET + `lance` not tested; recall/precision of the hybrid path not independently measured; internal DuckDB↔Lance Arrow C Data Interface plumbing described at the capability level, not traced in code.

## Revision — 2026-07-08: grounded against the actual Kontext implementation

The findings above are the generic capability analysis. After reading the real Kontext code (via a repo sweep of `src/KurrentDB.Kontext*`), the recommendation sharpens materially. The load-bearing facts:

- **Kontext already quantizes.** The shipping V1/V2 vector store uses USearch `2.23.0` configured `MetricKind.Cos` + **`ScalarKind.Int8`** (`src/KurrentDB.Kontext/Indexing/USearch/Segment.cs:32`), wrapped in a **custom LSM tree** (in-memory L0 brute-force buffer → sealed immutable mmap'd USearch segments → background level-merge; `USearchVectorStore.cs`, `Segment.cs`, `SegmentMerger.cs`). HNSW graph params are library defaults. Dimensions are probed at runtime — 384 for the local ONNX model (`Embeddings/EmbeddingService.cs:18`), 1536 for OpenAI.
- **The vector interface is append-only.** `IVectorStore` (`Indexing/IVectorStore.cs:8-12`) has **no update/delete/upsert** — dedup is a recovery probe + merge-time `Contains`. Memory revisions/retractions are handled above the index (event log + read-time filtering), never by mutating the ANN index. The LSM merge reclaims space.
- **FTS is Lucene.NET** `4.8.0-beta00017` with `BM25Similarity` (`Indexing/LuceneFtsStore.cs:35-38`).
- **V3/Engram is greenfield.** Its recall pipeline is a stub (`KurrentDB.Kontext.V3/Implementation/Components/RecallEngine.cs:10`) planned to fuse three retrievers via **RRF**; DuckDB.NET `1.5.0` + `Kurrent.Surge.DuckDB` are already in `Directory.Packages.props`. The DuckDB+Lance evaluation is really a **V3 decision**, not a V1/V2 retrofit.

**Corrected conclusions for Kontext specifically:**

1. **IVF_FLAT is a RAM regression here, not an upgrade.** Because Kontext is already on int8, swapping to Lance IVF_FLAT (full-precision f32) roughly **4× the vector footprint** (@384-dim: ~384 B → ~1536 B/vec; @1536-dim: ~1536 B → ~6144 B/vec). VSS/HNSW is worse still (graph + f32, RAM-resident). **The RAM-minimizing option is IVF_PQ, which neither the Lance extension's SQL (`IVF_FLAT` only) nor VSS (`HNSW` only) exposes.** Kontext's current int8 LSM is already at the frugal end; only native-built IVF_PQ (or pushing USearch to `b1` binary quantization) beats it.
2. **The delete/regeneration problem is one Kontext currently avoids.** VSS/Lance row-level `DELETE` is lazy-marked → stale index → manual `PRAGMA hnsw_compact_index`/`ALTER INDEX … OPTIMIZE`/rebuild. Kontext's append-only + LSM-merge design sidesteps this entirely. If migrating, **keep the append-only + tombstone-at-read model** rather than adopting row-level DML against the ANN index; Lance's `ALTER INDEX … OPTIMIZE` maps onto the existing background-merge concept.
3. **RRF mismatch.** Lance's built-in `lance_hybrid_search` fuses by **alpha-weighted linear scoring, not RRF** — so V3's RRF plan would call `lance_vector_search` + `lance_fts` separately and fuse in app code anyway, making the vector-index choice an isolated, independently-benchmarkable decision.

**Single most important spike:** confirm whether the DuckDB `lance` extension will *use* a natively-built (Python/Rust) **IVF_PQ** index at query time (the extension only *creates* IVF_FLAT in SQL). If yes, IVF_PQ-via-native-build is the RAM-frugal path; if no, DuckDB-driven vector search is at best lateral to the current int8 store.

### PoC result — 2026-07-08: **CONFIRMED — DuckDB queries a natively-built IVF_PQ Lance dataset via the ANN index**

Ran a local PoC (`scratchpad/lance_pq_poc.py`): `pip install pylance==8.0.0 duckdb==1.5.4 pyarrow==24.0.0`, built a 5000×384 Lance dataset, created an **IVF_PQ** index natively (`lance.write_dataset(...)` + `ds.create_index("vector", index_type="IVF_PQ", num_partitions=64, num_sub_vectors=96, metric="cosine")`), then queried it from DuckDB with the `lance` core extension.

- **`INSTALL lance; LOAD lance;` succeeded** on macOS arm64 / DuckDB 1.5.4.
- **`lance_vector_search(..., use_index => true)` returned results identical to native Lance IVF_PQ search — top-5 ids and distances matched exactly (5/5).**
- **`EXPLAIN` proves the ANN index is actually used** (not a flat fallback): the plan contains `ANNSubIndex: name=vector_idx, k=20, metric=Cosine` and `ANNIvfPartition: uuid=84491b65-…, minimum_nprobes=16` — i.e. it executes an IVF partition scan against the natively-built `vector_idx`.
- **No format skew** in this pairing: a dataset written by pylance 8.0.0 was read cleanly by DuckDB 1.5.4's embedded Lance. (Only one version pairing tested — pin both in production.)
- **Caveat found — distance-metric inconsistency:** `use_index => false` (brute-force) returned distances exactly **2× larger** than the index path (L2-squared `2·(1−cos)` vs cosine `1−cos`). Rankings coincided (normalized vectors), but the raw `_distance` semantics differ between the index and flat paths — **standardize on `use_index => true`** and don't compare `_distance` across the two.

**Consequence:** the RAM-frugal architecture is viable — store vectors in a Lance dataset, build **IVF_PQ** natively (Python/Rust), and serve ANN queries through DuckDB (DuckDB.NET). The remaining real costs are unchanged: the index *build* step still needs Python/Rust (no official .NET Lance SDK — check the community binding), periodic re-index on churn, and a real recall/latency benchmark on Kontext-scale data (the PoC used random vectors, so its recall numbers are meaningless).

### PoC (b) result — 2026-07-08: **CONFIRMED — full HYBRID path works over one Lance dataset** (`scratchpad/lance_hybrid_poc.py`)

Extended the PoC to build **both** native indexes on a single 5000-row Lance dataset — `IVF_PQ` on the vector column *and* `INVERTED` (BM25) on a text column (`ds.create_scalar_index("text", "INVERTED")`) — then drove all three retrieval modes through DuckDB. A rare token (`kurrent`) was injected into exactly rows {0,100,200,300} to make FTS falsifiable. **All three passed:**

- **`lance_vector_search`** → top hit = the query row (IVF_PQ ANN, as in PoC (a)).
- **`lance_fts('…','text','kurrent')`** → returned **exactly** rows {0,100,200,300} with real BM25 `_score` (~6.83). The native `INVERTED` index is used by DuckDB and produces genuine BM25 ranking — a functional stand-in for Lucene's `BM25Similarity`.
- **`lance_hybrid_search`** (vector=row0 + text=`kurrent`, `alpha=0.5`) → row 0 (matches **both**) ranked #1 with `_hybrid_score=1.0`; text-only rows {100,300} at `0.5`; vector-only rows at ~`0.02–0.04`. Returns fused `_hybrid_score` plus component `_distance` and `_score` (NULL for the modality a row didn't hit). `EXPLAIN` shows a single `LANCE_HYBRID_SEARCH` operator with `Search Mode: hybrid`, `Alpha: 0.5`, both columns bound.

**Fusion is confirmed alpha-weighted normalized score fusion, NOT RRF** — the clean `1.0 / 0.5` values are `alpha·vec_norm + (1−alpha)·text_norm`, not reciprocal-rank sums. So a single DuckDB + Lance dataset **does** cover vector + FTS + hybrid, functionally replacing USearch + Lucene. **But** if V3/Engram specifically wants RRF, it must call `lance_vector_search` + `lance_fts` separately and fuse ranks itself; the built-in DuckDB-extension fusion is score-based. Both native indexes were built with pylance; the entire query/hybrid surface is pure DuckDB SQL (DuckDB.NET-friendly).

### Investigation (a) — 2026-07-08: the community C# SDK removes the Python build dependency (and adds RRF)

The unofficial C# SDK **`LanceDB`** (`github.com/lennylxx/lancedb-csharp`, NuGet `LanceDB`, **v2.5.0**, released 2026-07-05; 10★/2 forks; Apache-2.0; 154 commits) wraps the official Rust `lancedb` crate via P/Invoke and, per its README, exposes exactly what the RAM-frugal plan needs — **from C#, no Python/Rust toolchain**:

- **IVF_PQ (and IVF-Flat/SQ/RQ, HNSW-PQ/SQ) index creation:** `await table.CreateIndex(new[]{"vector"}, new IvfPqIndex { DistanceType = DistanceType.Cosine });`. *Caveat:* the README shows only `DistanceType`; the PQ compression knobs (`NumPartitions`/`NumSubVectors`/`NumBits`) that drive the RAM win are **not documented** there — must be verified in the actual package before relying on them.
- **FTS/INVERTED (BM25):** `new FtsIndex { WithPosition = true, Language = "English" }` + `NearestToText(...)`.
- **Hybrid search WITH rerankers — including RRF:** `RRFReranker()`, `LinearCombinationReranker(weight)`, `MRR`. **This is the RRF that the DuckDB `lance_hybrid_search` function does *not* offer** — so V3/Engram's RRF requirement is satisfiable natively via this SDK.
- **Prebuilt native libs:** Linux x64, Windows x64, macOS arm64 — **no Linux ARM64** (a gap DuckDB.NET does *not* have). Targets **.NET 8.0+ / .NET Standard 2.0**.
- Underlying Rust crate version **not pinned/stated** — version-skew risk persists.

**Architectural consequence — two viable C# paths, not mutually exclusive:**
1. **DuckDB.NET → `lance` extension** (PoC-proven): full SQL engine (joins/analytics/filters) over the Lance data; fusion is alpha-weighted (no RRF); ships Linux arm64.
2. **`LanceDB` C# SDK direct** (P/Invoke): build IVF_PQ + FTS *and* query vector/FTS/hybrid **with RRF** entirely in C# — **eliminates the cross-language index-build step**, the main cost flagged earlier; but 10★ bus factor, no Linux arm64, undocumented PQ tuning knobs.

Net: you can build the RAM-frugal IVF_PQ index in C# (SDK) and still query it via DuckDB.NET (both read the same Lance dataset), or use the SDK end-to-end for retrieval and reserve DuckDB for analytical SQL. The remaining gating items are unchanged: real recall/latency benchmark on Kontext data, confirm PQ compression knobs are reachable, decide arm64 deployment, and pin Lance versions across whichever readers/writers touch the dataset.

### PoC (item 1) result — 2026-07-08: **CONFIRMED — PQ compression IS tunable from C#** (`scratchpad/lancedb_csharp_probe.cs`)

A .NET 10 file-based app referencing `LanceDB` 2.5.0 reflected over the managed option types (no native lib needed). Findings:

- **`IvfPqIndex` exposes all PQ knobs:** `NumPartitions` (int?), `NumSubVectors` (int?), `NumBits` (int), `DistanceType`, plus `MaxIterations`, `SampleRate`, `TargetPartitionSize`. So the compression that drives the RAM win **is fully controllable from C#** — the README just under-documented it. **Item (1): PASS.**
- **`DistanceType` enum = `{ L2, Cosine, Dot, Hamming }`** — includes `Cosine` (Kontext's current metric) and `Hamming` (for binary-quantized paths).
- **`HnswPqIndex` also exists from C#** (adds `EfConstruction`, `NumEdges` on top of the PQ knobs) — a graph+PQ option if IVF_PQ recall/latency proves insufficient.
- **`FtsIndex` tokenizer is Lucene-grade:** `Stem`, `RemoveStopWords`, `Language`, `BaseTokenizer`, `Ngram{Min,Max}Length`, `AsciiFolding`, `LowerCase`, `WithPosition`, `PrefixOnly`.
- **RRF is a real, tunable reranker:** `RRFReranker(float k, string returnScore)`, plus `LinearCombinationReranker(float weight, float fill, …)` and `MRRReranker`. This is the RRF V3/Engram wants — available in the C# SDK even though the DuckDB `lance_hybrid_search` function is alpha-weighted only.

**Bottom line after all four PoCs:** a single Lance dataset can back vector (IVF_PQ, RAM-frugal, C#-tunable) + FTS (BM25, Lucene-grade tokenizer) + hybrid (RRF via SDK, or alpha-fusion via DuckDB) — replacing USearch **and** Lucene — reachable entirely from C# via the `LanceDB` SDK for writes/index-build and DuckDB.NET for SQL/analytics, both over the same on-disk data. Residual risks are now narrow: SDK bus factor (10★), no Linux arm64 prebuilt in the SDK (DuckDB.NET has it), unpinned Rust-crate version, and — the only thing still unmeasured — **recall/latency on real Kontext embeddings at scale.**

### Required next step — benchmark the PoC on real embeddings (NOT YET DONE)

All four PoCs validated **plumbing only**; they used **random vectors**, so every recall/precision number in them is meaningless. Before any go/no-go on replacing USearch + Lucene, a benchmark is **required**, measuring the actual RAM-vs-quality trade for Kontext:

- **Data:** real embeddings from Kontext's embedding path (local ONNX model, 384-dim; and/or the cloud 1536-dim model), on a representative corpus at a realistic order of magnitude (≥ hundreds of thousands of vectors, per the LSM design's implied scale) with real memory/event text for the FTS side.
- **Vector metrics:** recall@k (10/100) of IVF_PQ **vs exact/brute-force ground truth**, swept across `NumSubVectors` and `NumBits` (4 vs 8) and `NumPartitions`/`nprobes`/`refine_factor`; query p50/p99 latency; and **on-disk + resident index size vs the current USearch int8 baseline** (this is the whole point — prove PQ actually beats int8 on RAM without unacceptable recall loss).
- **FTS parity:** compare `lance_fts`/SDK BM25 ranking against the current Lucene.NET `BM25Similarity` on the same queries.
- **Hybrid quality:** compare DuckDB alpha-fusion vs SDK RRF reranking for retrieval quality on Kontext-style mixed keyword+semantic queries.
- **Path parity:** confirm the DuckDB.NET read path and the `LanceDB` SDK read path return equivalent results over the same dataset, and re-check the `use_index=true` vs `false` distance-metric discrepancy noted above.
- **Write/maintenance:** upsert/delete + periodic re-index throughput under Kontext's write pattern (or confirm the append-only + tombstone model is retained).

Until this benchmark exists, the conclusion is "**functionally proven, performance unverified**" — do not treat the migration as validated on the PoCs alone.
