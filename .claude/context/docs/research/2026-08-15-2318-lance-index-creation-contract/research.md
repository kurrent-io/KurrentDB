---
title: Lance Index Creation Contract (DuckDB Extension)
type: research            # research | spike | investigation
date: 2026-08-15
author: sergio
tags: [kontext, lance, duckdb, fts, tokenizer, vector-index, retrieval]
---

# Research — Lance Index Creation Contract (DuckDB Extension)

## Question

What can we configure when we create Lance FTS (`USING INVERTED`) and vector indexes through the
vendored `lance-duckdb` extension? Which tokenizers can we use? The retrieval ranking work needs
the full contract before we tune the indexes.

Everything below is source-cited or live-verified against the vendored build
(engine DuckDB 1.5.5, fork `~/dev/contrib/lance-duckdb`, `lance` / `lance-index` 9.0.0).

## Findings

### How `WITH (...)` reaches Lance

The SQL layer (`lance-duckdb/src/lance_index.cpp:430-457`) consumes four keys and forwards the rest:

| Key | Behavior |
|-----|----------|
| `replace` | consumed — drop-and-recreate |
| `train` | consumed — default true |
| `retrain` | consumed — default false |
| `params = '<json>'` | consumed — RAW JSON forwarded wholesale, bypasses the k=v layer |
| anything else | passed into the params JSON verbatim (quoted→string, true/false→bool, number→number) |

There is no validation at the SQL layer. Lance deserializes the JSON on top of ITS OWN defaults
(`lance-index tokenizer.rs:223-333`), so an omitted knob keeps the upstream default. The fork's
hardcoded fallback `{"base_tokenizer":"simple","language":"English","stem":false}`
(`rust/ffi/index.rs:665-672`) applies ONLY when the SQL carries no params at all — its single
downgrade from upstream is `stem=false`.

### FTS: the analyzer pipeline

Filter order is fixed (`tokenizer.rs:910-928`); index side and query side both run it:

```
┌──────┐ ┌──────────────┐ ┌───────────┐ ┌───────┐ ┌──────┐ ┌───────────┐ ┌────────────┐ ┌──────────┐
│ text │ │ simple split │ │ len <= 40 │ │ lower │ │ stem │ │ stopwords │ │ ascii fold │ │ postings │
│      ├►│              ├►│           ├►│       ├►│      ├►│           ├►│            ├►│          │
└──────┘ └──────────────┘ └───────────┘ └───────┘ └──────┘ └───────────┘ └────────────┘ └──────────┘
```

### FTS: available `base_tokenizer` values

Eight exist in lance-index 9.0.0 (`tokenizer.rs:971-1025`). Six are compiled into our build and all
six were verified live in the duckdb CLI. `lindera/*` and `jieba/*` are feature-gated
(`tokenizer-lindera`, `tokenizer-jieba`); lance-index has NO default features and the fork enables
none (`lance-duckdb/Cargo.toml:25`), so both fail at create time with "unknown base tokenizer"
(`tokenizer.rs:1021`).

| Value | Status | What it does | Live probe |
|-------|--------|--------------|------------|
| `simple` | CURRENT | splits on whitespace AND punctuation | `deployed` hits "deployed!" and stem-matches "deployments" |
| `whitespace` | works | whitespace only — punctuation stays glued to tokens | `deployed` MISSES a doc containing "deployed!" |
| `raw` | works | whole value = one token | exact-string match only |
| `ngram` | works | character n-grams; `min/max_ngram_length` default 3/3, `prefix_only` false | partial `eployme` HITS; typo `deploymints` HITS |
| `code` | works | code-aware lexer; profile turns stem + stop words OFF | `writer` hits `OpenLanceWriter` with `split_identifiers=true` |
| `icu` | works | ICU dictionary segmentation (CJK, no-whitespace scripts); stop filter becomes all-language | English behaves like simple |
| `icu/split` | works | ICU + simple-style delimiter splitting | — |
| `lindera/*`, `jieba/*` | COMPILED OUT | Japanese / Chinese tokenizers | create fails: unknown base tokenizer |

### FTS: every creation knob

Upstream text-profile defaults from `tokenizer.rs:555-578`; input-only fields from
`RawInvertedIndexParams` (`tokenizer.rs:176-210`).

| Knob | Default | Meaning |
|------|---------|---------|
| `analyzer` | `text` | preset `text` or `code`; expands profile defaults, explicit knobs override (`:639-646`) |
| `lance_tokenizer` | infer | doc-level extraction: `text` or `json` (index JSON string documents) |
| `base_tokenizer` | `simple` | table above |
| `language` | `English` | stemmer + stop list. 18 snowball languages (`lance-tokenizer stemmer.rs:16-35`): Arabic, Danish, Dutch, English, Finnish, French, German, Greek, Hungarian, Italian, Norwegian, Portuguese, Romanian, Russian, Spanish, Swedish, Tamil, Turkish |
| `stem` | true | morphological normalization, both sides |
| `remove_stop_words` | true | language stop list |
| `custom_stop_words` | none | string list; replaces the built-in list |
| `ascii_folding` | true | diacritics → ASCII |
| `lower_case` | true | case folding |
| `max_token_length` | 40 | longer tokens dropped; null = unlimited |
| `with_position` | false | store term positions (phrase-query fuel); index grows; nothing queries positions today |
| `min_ngram_length` / `max_ngram_length` / `prefix_only` | 3 / 3 / false | ngram tokenizer only |
| `split_identifiers` / `split_on_numerics` / `preserve_original` / `index_operators` | false / true / true / false | code tokenizer only; code profile also sets stem=false, stop=false (`:597-607`) |
| `block_size` | 128 | posting block size; 128 or 256 only |
| `memory_limit` / `num_workers` / `format_version` | auto | build-time only, not persisted (`:136-172`) |

Kontext's current DDL (`KontextSchema.cs:69,100`):
`WITH (replace = true, base_tokenizer = 'simple', language = 'English', stem = true)` — resolves to
EXACTLY the upstream text defaults. Tokenizer-parity digging against the fork is exhausted.

### Vector: every creation knob

Types the fork accepts (`rust/ffi/index.rs:676-687`): `IVF_FLAT`, `IVF_PQ`, `IVF_SQ`, `IVF_RQ`,
`IVF_HNSW_FLAT`, `IVF_HNSW_PQ`, `IVF_HNSW_SQ`. Same SQL-level keys as FTS.

| Knob | Applies to | Default | Meaning |
|------|-----------|---------|---------|
| `metric_type` | all | `l2` | `l2`/`euclidean`, `cosine`, `dot`, `hamming` (`lance-linalg distance.rs:195-198`) |
| `version` | all | `v3` | index file format: `legacy` or `v3` (`lance vector.rs:242-250`) |
| `num_partitions` | all | 256 | IVF cluster count — the recall/speed lever |
| `num_bits` | PQ / SQ / RQ / HNSW_PQ / HNSW_SQ | 8 | quantization bits |
| `num_sub_vectors` | PQ, HNSW_PQ | 16 | PQ segmentation — compression vs fidelity |
| `max_iterations` | PQ, HNSW_PQ | 50 | kmeans training iterations |
| `sample_rate` | SQ, HNSW_SQ | 256 | training sample rate |
| `hnsw_m` | HNSW_* | 20 | graph degree (`hnsw builder.rs:85-93`) |
| `hnsw_ef_construction` | HNSW_* | 150 | build-time beam width |
| `hnsw_max_level` | HNSW_* | 7 | graph levels |
| `hnsw_prefetch_distance` | HNSW_* | 2 | scan prefetch; null disables |

### Search-time knobs (different surface, same mission)

Registered named parameters (`lance_search.cpp:955-961, 1626-1628, 1641-1647`):

| Function | Named args |
|----------|-----------|
| `lance_vector_search` | `k`, `nprobs` (THAT spelling), `refine_factor`, `prefilter`, `use_index`, `explain_verbose`, `filter` |
| `lance_fts` | `k`, `prefilter`, `filter` |
| `lance_hybrid_search` | vector's set plus `alpha`, `oversample_factor` |

`use_index := false` forces an exact scan — the ground-truth baseline for measuring ANN loss.
`nprobs` and `refine_factor` trade latency for recall at query time.

## Implications

- **Tokenizer choice for English conversational memories:** `simple` (current) is correct. `ngram`
  is the only alternative with recall upside (typo/partial tolerance, precision cost). `code` with
  `split_identifiers` fits the RECORDS table (agent-session content is code-heavy). `icu` matters
  only for multilingual memories. `whitespace`/`raw` are downgrades for prose.
- **The fork's FTS default is a stem-only downgrade** — everything else was already upstream
  default. Our explicit `stem = true` restores full parity; there is nothing further to recover.
- **Unmeasured lead:** `embedding_ivx` is `IVF_HNSW_PQ` with all defaults
  (`KontextIndexMaintenance.cs:49`) — 256 partitions + PQ on a 419-row corpus (≈1.6 rows per
  partition). ANN loss on the vector leg is plausible; `use_index := false` A/B bounds it in one
  benchmark row.
- Related: [[../../2026-08-02-0042-lance-duckdb-containment-pushdown/research.md]] (prefilter and
  containment pushdown), `project/duckdb-lance.md` (engine integration reference),
  `project/kontext-retrieval-pipeline.md`.
