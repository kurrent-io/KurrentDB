---
title: "lance_hybrid_search semantics: k, LIMIT, pushdown and the blended score"
type: research
date: 2026-08-21
author: sergio
tags: [kontext, lancedb, retrieval, duckdb, memory]
---

# Research — lance_hybrid_search semantics: k, LIMIT, pushdown and the blended score

> Everything below is measured by probes in `Kurrent.Kontext.Tests/Integration/Data/`, not read from
> documentation. Several of the store's own comments turned out to describe behaviour that the
> LanceDB extension's containment-pushdown fix had already made obsolete.

## Question

`KontextMemoryDataStore` wrapped `lance_hybrid_search` with three things it assumed it needed: a
candidate pool of `k = Math.Max(K, Limit)`, a trailing `LIMIT $limit`, and an outer
`WHERE is_superseded = false`. All three predate the pushdown fix in the LanceDB extension.

1. Does the SQL `LIMIT` change anything, or does `k` alone set the page?
2. Does a tag filter inflate the candidate pool, as the options docs claimed?
3. Is `is_superseded = false` pushed into the engine, or applied above its `k` rows?
4. What does the blended score actually mean, and can a threshold be drawn on it?

## Findings

### 1. `k` is the page. `LIMIT` was dead SQL.

`LanceHybridSearchSemanticsProbeTests`, raw SQL with the `LIMIT` clause removed:

| `LIMIT` clause | rows returned |
|---|---|
| none | 10 |
| 1 | 1 |
| 5 | 5 |
| 10 | 10 |
| 100 | **10** |

The engine returns exactly `k` rows. `LIMIT` can only trim, never extend. Because the store computed
`k = Math.Max(K, Limit)`, `limit ≤ k` always held — so the clause never even trimmed. It was inert in
every configuration the store could produce.

`Math.Max(K, Limit)` was itself a leftover: oversampling to work around tag filters not pushing down.
That bug is fixed, so the workaround was compensating for nothing.

### 2. Tag containment pushes down as a true prefilter

`TagPrefilterPushdownProbeTests` — 1000 rows, only 10 tagged `user:sergio`, the other 990 written to
match the query almost verbatim. Default `K = 10`.

| Query | Result |
|---|---|
| unfiltered, top 5 | all from the 990-row majority; **zero** of mine |
| tag-scoped, limit 5 | **5 of mine** |
| tag-scoped, full-text, limit 5 | **5 of mine** |

A post-filter over a top-10 drawn from 990 better matches could only have returned 0. A full page from
a 10-row minority is only possible if ranking never saw the majority.

Cost confirms it — the filter *shrinks* the work:

| | no tag | tag-scoped |
|---|---|---|
| 1000 rows | 6.5 ms | **5.0 ms** |
| 200 rows | 4.4 ms | 4.6 ms |

Scoping is cheaper than not scoping at scale, and free at small corpus sizes. The options docs claiming
`K` is "raised to the table's row count when tag filters apply (containment is not pushed down)" were
stale on all three options classes.

### 3. `is_superseded = false` also pushes down

Same probe, `k = 10`, 200 rows, superseded share swept to the extremes:

| superseded | live | no `WHERE` | with `WHERE` |
|---|---|---|---|
| 0 | 200 | 10 | 10 |
| 100 | 100 | 10 | 10 |
| 180 | 20 | 10 | 10 |
| 195 | 5 | 10 | **5** |
| 198 | 2 | 10 | **2** |
| 200 | 0 | 10 | **0** |

Every row returns exactly `min(k, live)`. At 195/200 superseded a post-filter over 10 mixed rows would
have yielded ~0; it yielded 5. The predicate is evaluated inside candidate selection, so superseded
rows never consume slots and a page never comes back short.

### 4. The blended score cannot carry a threshold

`_hybrid_score` is `alpha·vector + (1−alpha)·keyword`. Sweeping alpha against one fixed pair
(`HybridScoreStabilityProbeTests`):

| alpha | score |
|---|---|
| 0.00 | 0.5000 |
| 0.25 | 0.6250 |
| 0.50 | 0.7500 |
| 0.75 | 0.8750 |
| 1.00 | 1.0000 |

Perfectly linear — that pair scores 0.5 on the keyword leg and 1.0 on the vector leg. Which produces
the collision that kills any cut:

| | keyword leg | vector leg | score at α=0.5 |
|---|---|---|---|
| semantic duplicate (no shared words) | ~0 | high | **≈0.5** |
| lexical stranger (shared words, wrong meaning) | high | ~0 | **≈0.5** |

Same number, opposite meaning — arithmetic, not noise. Measured directly in
`RelatedFloorAndPipelineProbeTests`: worst true duplicate `0.5000`, best non-duplicate `0.5000`.
**No threshold separates them.**

The score IS stable across pool size — unchanged from `k = 5` to `k = 200` — so it is not normalised
over the candidate pool. The ambiguity is created purely by blending two legs into one scalar.

### 5. Duplicate-detection tuning

`RelatedPipelineTuningProbeTests`, MRR over 6 lexical and 6 semantic planted duplicate pairs in 300 rows:

| alpha | reranker | lexical | semantic | overall |
|---|---|---|---|---|
| 0.00 | none | 1.000 | **0.000** | 0.500 |
| 0.25 | none | 1.000 | 0.750 | 0.875 |
| **0.45** | **none** | **1.000** | **0.833** | **0.917** |
| 0.45 | bm25 | 1.000 | 0.625 | 0.812 |
| 1.00 | none | 1.000 | 0.833 | 0.917 |

- Pure keyword scores **zero** on reworded duplicates. The vector leg is not optional.
- MRR plateaus from alpha 0.45 to 1.0 — no reason to deviate from the recall chain's pinned 0.45.
- `Bm25Reranker` **hurts in every row** (−0.208 semantic, no lexical gain). LanceDB's hybrid already
  blends BM25; a pool-local reread runs that leg twice with a narrower view.
- `CognitiveModulator` demotes a known duplicate out of first place — recency and importance are
  actively wrong for "does this already exist".

## Implications

**Applied to the code.**

- `Limit` removed from `VectorSearchOptions`, `FullTextSearchOptions` and `HybridSearchOptions`. `K` is
  the page size.
- `k = Math.Max(K, Limit)` → `k = options.K` in all three search modes.
- `LIMIT $limit` removed from the three search statements. **It stays in `ListAsync`**, which is a
  plain `SELECT` where it is genuinely load-bearing — removing it there broke a test and was restored.
- The `K` doc comments on all three options classes corrected; the stale pushdown claim is gone.

**Left alone deliberately.** `WHERE is_superseded = false` stays in the SQL even though the engine
already applies it. It is the predicate's only written statement of intent, and it costs nothing.

**For `related` / duplicate detection.** Use the store's hybrid `SearchAsync` directly at alpha 0.45,
scoped by tags. No retrieval pipeline — a bare `Planner + HybridSearch` chain returns an identical
ordering for 3× the latency (18.0 ms vs 5.9 ms), and every stage that exists makes duplicate detection
worse. No similarity threshold is possible; exact-content equality is the only safe automatic decision.

**For the tag scope.** Tag filtering is not just free, it is faster at scale — so scoping a neighbour
search by the stamped `user` tag has no cost argument against it, only the correctness argument for it.

## Probes

| File | Answers |
|---|---|
| `LanceHybridSearchSemanticsProbeTests` | k vs LIMIT, superseded pushdown, per-leg columns |
| `TagPrefilterPushdownProbeTests` | tag containment is a true prefilter |
| `HybridScoreStabilityProbeTests` | score stability vs k, and the alpha decomposition |
| `RelatedFloorAndPipelineProbeTests` | pipeline equivalence, floor viability |
| `RelatedPipelineTuningProbeTests` | alpha and reranker sweep by duplicate kind |
| `RelatedSearchCostProbeTests` | per-retain cost breakdown |

All at 200–1000 rows **without an ANN vector index**. Corpus scaling is unmeasured, and the tag
prefilter's advantage should grow with size while the raw scan cost also grows.
