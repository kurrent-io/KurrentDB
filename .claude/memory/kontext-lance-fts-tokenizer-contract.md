---
name: kontext-lance-fts-tokenizer-contract
description: "Probed 2026-08-15 — lance FTS tokenizer contract (fork default = upstream minus stem; our WITH restores full upstream defaults); hybrid chain is deterministic, two-leg RRF compositions are the noise source; three-way comparison numbers"
metadata: 
  node_type: memory
  type: project
  originSessionId: d2c6fd63-c002-48d7-bc13-c87c234a1875
  modified: 2026-08-15T22:23:31.843Z
---

Probed 2026-08-15 (vendored lance-duckdb fork + lance-index 9.0.0 crate source + live CLI probe).

**Tokenizer contract for `USING INVERTED`:**

- SQL side (`lance-duckdb/src/lance_index.cpp:442-459`): `WITH` keys `replace`/`train`/`retrain`
  are consumed by the extension; `params = '<json>'` passes raw JSON wholesale (every
  lance-index knob reachable); EVERY other key passes through into the params JSON verbatim
  (typed: quoted→string, true/false→bool, number→number). No validation at the SQL layer.
- Fork fallback (`rust/ffi/index.rs:665-672`): applies ONLY when the SQL carries NO params —
  `{"base_tokenizer":"simple","language":"English","stem":false}`. Deserialization starts from
  upstream defaults and overlays these, so the fallback's ONLY downgrade is `stem=false`.
- Upstream text-profile defaults (`lance-index-9.0.0 tokenizer.rs:555-578`): base_tokenizer
  simple, language English, **stem TRUE, remove_stop_words TRUE, ascii_folding TRUE**,
  lower_case true, max_token_length 40, with_position false. Filter order in build():
  RemoveLong(40) → LowerCase → Stem → StopWords → AsciiFolding (`tokenizer.rs:910-928`).
- Consequence: KontextSchema's `WITH (base_tokenizer='simple', language='English', stem=true)`
  = EXACT upstream defaults. There is nothing left to recover on the tokenizer axis —
  "FTS parity digging" is exhausted. Keyword-only recall@5 0.15-0.21 on the LoCoMo corpus is
  what corpus-wide BM25 delivers there.
- Query side (`rust/ffi/search.rs:132-135`): `lance_fts` builds a plain `FullTextSearchQuery`
  — OR-match BM25 top-k; query text is analyzed with the tokenizer persisted in the index.
- Live CLI probe pinned: stem=true matches 'run'→"running"; folding matches 'cafe'→"café";
  stop-word-only queries return 0 rows; missing OR terms don't kill a match. Fork-default
  index differs ONLY in the stem behavior (same scores otherwise).

**Determinism finding (NOT yet root-caused):** the Hybrid chain (single `lance_hybrid_search`
leg) reproduced recall@5/mrr/ndcg IDENTICAL to 4 decimals across two separate processes
(test run + benchmark run). The two-leg RRF compositions (Default, Legacy) wobble ±0.05
recall@5 across evaluations INCLUDING three same-process evaluations. Noise is isolated to
the two-leg fusion path; candidate mechanism (unverified): RRF tie handling under concurrent
leg arrival. Do not trust point floors on two-leg compositions until this is fixed.

**Three-way comparison (419 memories / 150 questions, limit 10, 2026-08-15):**
benchmark sweep — hybrid α0.3: 0.4656/0.3929/0.4164; α0.5: 0.4622/0.3874/0.4062;
α0.7: 0.4272/0.3259/0.3568; default: 0.3717/0.2763/0.3035 (noisy, 0.37-0.42 band);
legacy: 0.16-0.21. Head-to-head ndcg per question: hybrid α0.5 wins 46, default 14, ties 90.
Hybrid is also fastest (~14-16ms vs 16.5 default, 19.2 legacy). Production ships Hybrid
(`KontextMemoryWireUp.CreateDefaultRetriever`). `RetrievalRankingTests.shipped_hybrid_beats_the_legacy_baseline`
(green) carries the three-way report; `Kurrent.Kontext.Benchmarks` Program sweeps α 0.3/0.5/0.7.

**Full index-creation surface (probed + source, 2026-08-15):**
- base_tokenizer values COMPILED into the vendored build (all live-verified): simple,
  whitespace, raw, ngram (min/max_ngram_length default 3/3, prefix_only), code (+
  split_identifiers/split_on_numerics/preserve_original/index_operators; profile turns stem
  and stop words OFF), icu, icu/split. `lindera/*` and `jieba/*` REJECTED at create time —
  feature-gated, lance-index has no default features and the fork enables none.
- Other INVERTED knobs: analyzer (text|code preset), lance_tokenizer (text|json),
  language (18 snowball languages, lance-tokenizer stemmer.rs:16-35), with_position,
  custom_stop_words, block_size (128|256), memory_limit/num_workers/format_version
  (build-only). SQL-level: replace/train/retrain consumed; `params='<json>'` raw door.
- Vector types (fork index.rs:676-687): IVF_FLAT, IVF_PQ, IVF_SQ, IVF_RQ, IVF_HNSW_FLAT,
  IVF_HNSW_PQ, IVF_HNSW_SQ. Common: metric_type l2|euclidean|cosine|dot|hamming (default
  l2), version legacy|v3, num_partitions 256. PQ: num_bits 8, num_sub_vectors 16,
  max_iterations 50. SQ: num_bits 8, sample_rate 256. HNSW: hnsw_m 20,
  hnsw_ef_construction 150, hnsw_max_level 7, hnsw_prefetch_distance 2.
- Search-time named args (lance_search.cpp:955-961,1626-1628,1641-1647): vector = k,
  **nprobs** (that spelling), refine_factor, prefilter, use_index, explain_verbose, filter;
  fts = k, prefilter, filter; hybrid = vector set + alpha, oversample_factor.
- LEAD (unmeasured): Kontext creates embedding_ivx as IVF_HNSW_PQ with ALL defaults —
  256 partitions + PQ on a 419-row corpus ≈ 1.6 rows/partition; ANN loss on the vector leg
  is plausible and cheaply measurable via `use_index := false` A/B.

**HILL-CLIMB OUTCOME 2026-08-15 (Sérgio ordered "every measurement option, keep the objective
winner"):** 23-row greedy sweep (alpha fine, MMR, reranker weights, pool, engine knobs, ngram)
in `Kurrent.Kontext.Benchmarks`. Winner = **`Focused` chain: Hybrid α 0.45, BM25 reread +
modulation, NO MMR** — 0.4889/0.4019/0.4213 vs shipped hybrid α0.5 0.4622/0.3874/0.4062, better
on all three metrics, reproduced bit-identical across three processes. MMR (λ0.7) was COSTING
recall@5; λ=1.0 ≡ no-mmr exactly. Also settled: exact-scan ties the IVF_HNSW_PQ index (ANN loss
ZERO at this scale — refine 1/4/8 all identical), pool 30 stands (60 worse, 20 ties), reranker
defaults (w=2, K=10) stand, ngram FTS is WORSE for hybrid (0.4483). Landed: `Focused(...)` chain
in KontextRetrieverBuilderExtensions (convenience overload presets Alpha 0.45),
`focused_beats_the_shipped_hybrid` pins it (green). HybridSearch gained a `tune` ctor hook
(engine knobs); KontextStoreFixture/KontextCorpus expose `DataSources`.

**SHIPPED 2026-08-15 (Sérgio's ruling): production wires `Focused` — and it is deliberately NOT
configurable.** `Focused(index, generator, TimeProvider?)` pins alpha 0.45 + default reread +
modulation, no MMR, and reads NO options — the name is the benchmark claim; a tuned variant is a
different chain (compose `Hybrid(options)` or register your own `IKontextRetriever`, which beats
the default). `CreateDefaultRetriever` flipped, `ResolveServices` deleted,
`KontextRetrievalOptions` doc now says the shipped chain reads none of it. Verified: 198/198
Retrieval.Tests + 51/52 Kontext.Tests — the one failure is the standing
`default_pipeline_meets_the_ranking_floor` (RRF Default floor, pre-existing). Do not make
Focused configurable again.

**ROOT CAUSE FOUND 2026-08-16 — the "RRF nondeterminism" and the collapsed keyword leg were ONE
bug: `lance_fts` over UNINDEXED rows returns the FIRST k rows by scan arrival, NOT the top k by
BM25.** Probe-pinned: index created before inserts (rows_indexed=0) → a tripled-term needle
inserted last NEVER surfaces at k<matches and membership varies per scan; rebuild the index with
data present → true top-k, bit-stable. Kontext's bootstrap creates content_fts on the EMPTY
table (KontextSchemaTask), so every row lives in the unindexed tail — in the corpus fixture AND
IN PRODUCTION. The fuser was innocent (FusionAccumulator has a total order); Task.WhenAll
preserves leg order; concurrency was irrelevant (sequential repeats diverged, jaccard 0.159 on
the raw keyword leg). FIXED in the fixture: KontextCorpus.InitializeAsync rebuilds content_fts
(replace=true) after seeding. After the heal: EVERYTHING bit-stable (150/150 ties across
sequential triples, concurrent pairs, raw scans); keyword leg 0.13-0.22 random → 0.4167 stable;
Default RRF 0.37-0.42 random → 0.4700 stable — EXACTLY the old 1.5.3 floor calibration, so the
"engine ranking regression" never existed. Hybrid chains barely moved (their internal
oversampled legs masked it). Healed-index hill-climb: best = α0.40 + pool floor 60 + no MMR →
0.5000/0.3915/0.4203; α0.50-recheck → 0.4867/0.4019/0.4269 (best mrr/ndcg); ngram-fts variant
tops ndcg/mrr (0.4983/0.4074/0.4298) but needs a schema DDL change. Current pinned Focused
(α0.45/pool30/no-MMR) measures 0.4889/0.4006/0.4205 on the healed index — still beats shipped-
hybrid-α0.5 and Default.

**OPEN (Sérgio's calls):** (1) PRODUCTION carries the first-k wound — content_fts needs a
rebuild/optimize step after data lands (maintenance currently retrains vector indexes only);
(2) whether to recalibrate Focused (α0.40+pool60 recall-max vs α0.50 mrr/ndcg-max vs keep
α0.45) and whether ngram earns the schema change; (3) floor test should pass again post-heal —
verify, then the recalibrate-vs-move-gate question may be moot.
See [[kontext-kurrentdb-integration-exploration]].
