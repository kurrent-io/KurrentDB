---
title: Padding a Narrow Embedding Into a Wider Lance Vector Column
type: research            # research | spike | investigation
date: 2026-08-20
author: sergio
tags: [kontext, lance, duckdb, vector-index, ivf-pq, embeddings, retrieval]
---

# Research — Padding a Narrow Embedding Into a Wider Lance Vector Column

## Question

Can a 384-dimensional embedding live in a wider fixed-size column — `FLOAT[768]` — zero-padded, and
still retrieve with full effectiveness?

The motive is schema flexibility. A wider column would let one schema hold models of different
widths, instead of pinning the store to the width of the shipped model.

Everything below is measured, not reasoned. The probe ran against the vendored build the product
uses: `vendor/duckdb/extensions/v1.5.5/osx_arm64/lance.duckdb_extension`, engine DuckDB 1.5.5,
lance 9.0.0. The corpus is 10 000 unit-norm vectors in 20 clusters with 200 held-out queries, all
generated from DuckDB's `hash()` so the run reproduces exactly.

This extends [Lance Index Creation Contract](../2026-08-15-2318-lance-index-creation-contract/research.md),
which documents which index knobs exist. This doc measures what those knobs do when the declared
column width stops matching the real vector width.

## Findings

### 1. Padding is mandatory — the engine rejects width mismatch on both paths

```
WRITE   INSERT a 384-length list into FLOAT[768]
        → Conversion Error: Cannot cast array of size 384 to array of size 768

QUERY   lance_vector_search(384-dim query, FLOAT[768] column)
        → IO Error: query dim(4) doesn't match the column emb vector dim(8)
          (lance-9.0.0/src/dataset/scanner.rs:1552)
```

A misconfigured width cannot corrupt the store. The row is refused, not stored badly.

### 2. Exact search is bit-identical

200 queries at k=50, `use_index := false`, native `FLOAT[384]` against padded `FLOAT[768]`:

| Check | Result |
|---|---|
| Positions where the id differs | **0** / 10 000 |
| Positions where the distance differs | **0** / 10 000 |
| Max absolute distance delta | **0.0** |
| Top-1 agreement | 200 / 200 |

Zero padding adds nothing to a dot product and nothing to a norm, so L2, cosine and dot are all
unaffected. The result is identical, not approximately identical.

### 3. ANN recall degrades unless `num_sub_vectors` scales with the declared width

recall@10 against exact ground truth. IVF_PQ, `num_partitions = 1`, metric L2:

| Index shape | rf=1 | rf=5 | rf=20 |
|---|---|---|---|
| 🏆 padded 768, `nsub=96` (768/8 — 48 real codes) | 0.227 | 0.5755 | **0.931** |
| native 384, `nsub=48` (384/8 — 48 real codes) | 0.235 | 0.589 | **0.9245** |
| padded 768, `nsub=48` (24 real codes, 24 wasted) | 0.163 | 0.4445 | **0.826** |

The top two rows are a tie inside k-means seed noise — 4 hits out of 2000 separate them.

**Cause.** At `nsub=48` on a 768-wide column each sub-vector spans 16 dimensions, so sub-vectors
24–47 cover nothing but zeros. The 384 real dimensions then get 24 codebooks instead of 48 — half
the code budget, over coarser sub-spaces. The measured cost is **11% to 31% relative recall**,
worst at low refine factors.

Training does not fail and does not warn. An all-zero sub-space trains a degenerate quantizer
silently.

### 4. Cosine behaves the same as L2

At matched `nsub` and rf=20: native 0.9215, padded 0.9195. No metric-specific surprise.

### 5. The cost is permanent

| | native 384 | padded 768 | ratio |
|---|---|---|---|
| Dataset on disk | 17 MB | 35 MB | 2.06× |
| `_indices` on disk | 2.6 MB | 5.9 MB | 2.27× |
| Exact scan, 200 × k=50 | 1.33 s | 2.28 s | 1.71× |

Half of every stored vector and half of every PQ codebook hold zeros for the life of the store. No
index setting recovers that.

## Implications

**Widening is viable, never free.** Two conditions, both mandatory: pad at the embedding boundary on
write *and* query, and derive `num_sub_vectors` from the declared column width rather than the
model's true width.

**Kontext is currently correct by construction.** `KontextIndexConstants.VectorsDimension` feeds both
the DDL (`MemoriesInitialSchema.cs`, `RecordsInitialSchema.cs`) and the index sizing
(`KontextIndexJanitor.cs`, `KontextRecordsIndexer.cs` — both `VectorsDimension / 8`). Raising that one
constant to 768 yields `nsub=96` automatically. The degraded shape is only reachable if the column
width and that constant are allowed to diverge.

**Query-side padding belongs at the embedding boundary.** `KontextMemoryDataStore` casts the query to
`FLOAT[{queryEmbedding.Length}]` — the query's own length. A short query vector reaches the engine
unchanged and produces the dimension error above. Padding must happen before the SQL, not in it.

**A shared width does not make two models comparable.** This is the caveat that limits the whole
idea. If the wide column later holds real 768-dimensional vectors from another model, those rows and
the zero-padded 384 rows occupy one column but two different embedding spaces. Distances across that
boundary are arithmetically valid and semantically meaningless. Cross-model retrieval needs a column
per model, a table per model, or a discriminator plus a filtered search — the width is a container
decision, not a compatibility one.

## Reproducing

`refs/` holds the probe as it ran:

| File | Role |
|---|---|
| `01_build.sql` | deterministic clustered corpus + 200 held-out queries |
| `02_load.sql` | loads both the native and padded lance tables |
| `gen.sql` | emits the search legs (the lance table function rejects subqueries, so query vectors inline as literals) |
| `03_compare_exact.sql` | exact-search parity comparison |
| `04_recall.sql` | recall@10 against exact ground truth |

Run each against `duckdb -unsigned` with the vendored extension loaded and a lance namespace
attached as `ldb`. No RNG and no clock, so the numbers above reproduce exactly.
