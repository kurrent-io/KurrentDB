---
title: Kontext SQLite Search Backend Prototype
status: settling         # exploring | settling | superseded
authors: [sergio]
date: 2026-08-13
tags: [kontext, sqlite, sqlite-vec, fts5, vector-search, hybrid-search, prototype]
---

# Design Space — Kontext SQLite Search Backend Prototype

<!--
Working doc. Brainstorm, discussion, and decisions for this feature. Deliberately informal and
append-leaning — you add to it, you mark decisions, you do not rewrite the history of the discussion.
Kept for the life of the feature. Once it settles, distill the outcome into prd/prd.md and spec/spec.md,
and slice releases into plans/. This doc is also the feature's decision record — keep the rejected
options; the "why not" is the value. Sources this design space cites go in design/refs/.
-->

## Problem / Trigger

Kontext currently stores its memory and whole-log search models in Lance datasets and queries them
through in-memory DuckDB engines with the low-level DuckDB `lance` extension. The stack provides
BM25, vector search, hybrid search, scalar filters, and transactional writes, but it also carries a
young native extension, vendored binaries, index maintenance, Lance-specific storage behavior, and a
second embedded SQL engine.

KurrentDB already ships `Microsoft.Data.Sqlite` and `SQLitePCLRaw.bundle_e_sqlite3` for scavenging.
The question is whether SQLite FTS5 plus `sqlite-vec` can provide the Kontext search surface with a
smaller operational model and acceptable search quality.

This is a scoped option. Secondary Indexing and Schema Registry still use DuckDB. Replacing the
Kontext backend does not remove DuckDB from KurrentDB as a whole.

## Exploration

### Current Lance baseline

Both `memories` and `records` configure `IVF_HNSW_PQ` with squared L2 distance, one IVF partition,
8-bit PQ, one dimension per PQ subvector, HNSW `m=16`, and `ef_construction=100`. Search uses an
exact `refine_factor=4` rerank. Below the 256-row PQ training floor, Lance performs exact flat KNN.

One IVF partition means the current index gets no partition-pruning benefit. The effective shape is
HNSW graph traversal over PQ codes followed by exact reranking. New, unindexed rows are scanned
exactly and merged with indexed results.

Lance also supplies an `INVERTED` BM25 index, scalar `BTREE` indexes, and a `LABEL_LIST` tag index.
The DuckDB extension does not currently push tag containment into Lance, so KurrentDB requests a
candidate pool as large as the table and applies the tag predicate afterwards.

### SQLite capability probe

An isolated .NET 10 file-based probe used the same packages already pinned by KurrentDB:

- `Microsoft.Data.Sqlite` 10.0.10.
- `SQLitePCLRaw.bundle_e_sqlite3` 2.1.12.
- Upstream precompiled `sqlite-vec` 0.1.10-alpha.4 for macOS ARM64.

The probe loaded `sqlite-vec` through `SqliteConnection.LoadExtension` and reported SQLite 3.53.3
with FTS5, NEON, rescore, and DiskANN enabled. It passed BM25 search, float-vector BLOB binding,
cosine KNN, scalar metadata prefiltering, SQL RRF fusion, transactional rollback across the content
row plus FTS5 plus `vec0`, vector update, restart persistence, and `PRAGMA integrity_check`.

### SQLite vector choices

`sqlite-vec` offers four relevant execution strategies:

| Strategy | Search shape | Precision | Filter support | Release state |
|---|---|---|---|---|
| Flat float `vec0` | Full vector scan | Exact | Metadata and partition filters | Precompiled |
| Quantized flat | Full int8 or binary scan | Approximate vs float | Metadata possible on flat tables | Precompiled |
| Rescore | Quantized full scan, then float rerank | Approximate shortlist, exact final distances | No metadata or partition columns | Precompiled |
| DiskANN | Quantized graph traversal, then float rerank | ANN | No metadata or partition columns | Precompiled, alpha |
| IVF | Centroid partitions with optional int8/binary quantization | ANN | No metadata or partition columns | Source only, experimental and disabled in release binary |

SQLite cannot reproduce `IVF_HNSW_PQ` exactly with the released extension. DiskANN is the closest
graph ANN option. Rescore is the closest quantize-then-rerank option, but it still scans all vectors.
The experimental IVF path is the closest partitioned option, but it does not include HNSW or PQ and
requires a custom native build.

### Candidate architecture

Use one SQLite database per derived Kontext index: `memories.sqlite` and `records.sqlite`. Each file
contains the content table, an external-content FTS5 table, the `vec0` table, normalized tag rows
where needed, and the projection checkpoint. Separate files preserve the current independent writer
lanes and avoid unnecessary contention under SQLite's single-writer model.

Start with exact flat float `vec0`. It gives a correctness oracle and supports scalar prefilters.
Implement vector-only, BM25-only, and hybrid query modes behind the existing Kontext interfaces.
Fuse hybrid candidate sets in SQL or the existing retrieval pipeline. Benchmark alpha blending for
behavioral parity and RRF as the quality-oriented alternative.

Only add rescore or DiskANN after the exact implementation passes functional tests and provides a
measured baseline. Filtered ANN queries must oversample and post-filter, or route to the exact path.

## Decisions

- 2026-08-13 - Treat SQLite as an alternative Kontext backend, not a replacement for every DuckDB
  use in KurrentDB.
- 2026-08-13 - Build functional parity on flat float `vec0` before evaluating ANN. Exact KNN is the
  benchmark oracle and the safest implementation for filtered search.
- 2026-08-13 - Do not depend on SQLite IVF in the first prototype. The upstream release binary does
  not include it, and enabling it would make KurrentDB own a custom experimental native build.
- 2026-08-13 - Evaluate released DiskANN and rescore modes as optional second-stage experiments.
- 2026-08-13 - Keep all row, FTS, vector, tag, and checkpoint changes in one SQLite transaction.
- 2026-08-13 - Vendor release binaries with checksums for supported RIDs. Do not download native
  extensions at runtime.
- 2026-08-13 - Rebuild the SQLite index from KurrentDB's source events instead of converting Lance
  files. Both stores are derived read models.

## Open Questions

- What are the production row-count distributions for memories and records per node?
- What embedding dimension and normalization policy must the benchmark use?
- Is current Lance alpha blending a compatibility requirement, or may SQLite use RRF by default?
- What recall, latency, disk, and ingestion thresholds justify ANN over exact flat KNN?
- Can `vec0` rowid-set constraints express every multi-tag filter efficiently, or must some filtered
  searches use scalar distance functions over a relational candidate set?
- Does DiskANN mutation and recovery remain correct under the exact KurrentDB write workload?
- Which upstream `sqlite-vec` version and support policy would management accept for production?
