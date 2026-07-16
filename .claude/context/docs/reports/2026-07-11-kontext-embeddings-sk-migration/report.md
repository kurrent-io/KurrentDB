---
title: Kontext Embeddings — Replace the Hand-Rolled ONNX Tokenizer with Semantic Kernel BertOnnx
type: analysis
date: 2026-07-11
author: sergio
tags: [kontext, embeddings, onnx, semantic-kernel, tokenizer, aot]
scope: src/KurrentDB.Kontext/Embeddings (the local ONNX embedding path)
related: [2026-07-08-faiss-vectordata-connector-aot]
---

## Summary

Kontext's **local** embedding provider hand-rolls its own BERT tokenizer
(`WordPieceTokenizer`) and ONNX inference (`EmbeddingService`) behind
`IEmbeddingGenerator<string, Embedding<float>>`. This analysis empirically compares that
path, byte-for-byte, against **Semantic Kernel's `BertOnnx` connector** (which uses the
`FastBertTokenizer` library) driving the **same** ONNX model and vocab.

**Verdict: Semantic Kernel is a safe, strictly-higher-quality replacement.** Fed the same
`all-MiniLM-L6-v2` model + vocab, with `NormalizeEmbeddings = true` and
`CaseSensitive = false`, SK produces **bit-identical** embeddings to the hand-rolled path
for ASCII/Latin text (cosine `1.00000000`, max abs diff ≤ `1.5e-8`), and **fixes a real
correctness bug** the hand-rolled path has for accented text: it embeds `café` and `cafe`
to the *same* vector (cosine `1.0000`), where the hand-rolled path scores them a broken
`0.46`.

**Recommendation (revised after the OOTB research + head-to-head below; supersedes the
SK-first note this section originally carried):** stop hand-rolling the tokenizer and use
**`Microsoft.ML.Tokenizers`** (GA, first-party, empirically AOT-clean), keeping the existing
~40-line ONNX inference. Quick win: swap `WordPieceTokenizer` → `BertTokenizer` on all-MiniLM
(fixes the accent bug). Real goal: **`multilingual-e5-small`** (SentencePiece + fairseq remap +
ONNX) — the C# path is now **proven bit-exact** vs a transformers.js reference (min cosine
`1.000000` over 26 inputs). Gate on ICU (`InvariantGlobalization=false`). SK `BertOnnx` is a
fallback only (WordPiece-only, alpha). The **cross-encoder** also uses `WordPieceTokenizer` and
must be migrated alongside.

## Findings

### 1. What exists today (the local path)

`EmbeddingService : IEmbeddingGenerator<string, Embedding<float>>` runs `all-MiniLM-L6-v2`
via ONNX Runtime: tokenize with `WordPieceTokenizer` → ONNX → mean-pool over the attention
mask → L2 normalize (384-dim). `ModelManager` selects the AVX-512 vs AVX-2 INT8-quantized
model variant per CPU and loads model+vocab from the embedded `KurrentDB.Kontext.Models`
assembly. The hand-rolled `WordPieceTokenizer` does **no Unicode normalization and no
accent stripping**; it splits on `char.IsWhiteSpace/IsPunctuation/IsSymbol` and greedily
WordPiece-matches with `OrdinalIgnoreCase` lookups.

### 2. Equivalence on ASCII/Latin text — bit-identical

Same model + vocab fed to both. SK options: `PoolingMode = Mean` (default),
`NormalizeEmbeddings = true`, `CaseSensitive = false`.

| Input | cosine(HR, SK) | max abs diff | verdict |
|---|---|---|---|
| `hello world` | 1.00000000 | 1.5e-8 | identical |
| `The quick brown fox jumps over the lazy dog.` | 1.00000000 | 0 | identical |
| `KurrentDB is an event-native database.` | 1.00000000 | 0 | identical |
| `MixedCase HELLO hello WORLD` | 1.00000000 | 1.5e-8 | identical |
| `numbers 123 and symbols $100 #hash @at` | 1.00000000 | 0 | identical |
| `emoji test 😀🚀` | 1.00000000 | 0 | identical |
| `""` (empty) | 1.00000000 | 0 | identical |
| 600-token string (truncation @ 512) | 1.00000000 | 1.5e-8 | identical |

For English/Latin content the two are the same generator. Differences are float rounding.

### 3. Divergence on accented / non-Latin text — and why

| Input | cosine(HR, SK) |
|---|---|
| `Café résumé naïve` | 0.2468 |
| `Über GROSSE Straße` | 0.8557 |
| `北京欢迎你 東京` (CJK) | 0.6422 |

`all-MiniLM-L6-v2` is built on **`bert-base-uncased`**, whose training preprocessing
**always** lowercases, **strips accents** (NFD then drop combining marks), and **splits CJK
into per-character tokens**. The model's vocab was built for that. SK/FastBertTokenizer does
this preprocessing; the hand-rolled tokenizer does not. So `café` (hand-rolled) is looked up
verbatim, misses the vocab, and fragments into `ca` + `##f` + `[UNK]` — a token sequence the
model never associated with the word — producing a meaningless vector. SK strips the accent
to `cafe`, a clean vocab token, producing the vector the model was trained to emit.

**The divergence is the hand-rolled path being wrong, not SK.**

### 4. The accent bug, quantified (the reason to migrate)

An accented word and its plain form should embed to the *same place* for retrieval to work.
Cosine between each accented form and its plain form:

| Pair | hand-rolled | Semantic Kernel |
|---|---|---|
| `café` / `cafe` | 0.46 | **1.0000** |
| `résumé` / `resume` | 0.15 | **1.0000** |
| `naïve` / `naive` | 0.22 | **1.0000** |
| `José` / `Jose` | 0.40 | **1.0000** |
| `Zürich` / `Zurich` | 0.22 | **1.0000** |
| `Müller` / `Muller` | 0.12 | **1.0000** |
| `garçon` / `garcon` | 0.45 | **1.0000** |
| `Renée` / `Renee` | 0.32 | **1.0000** |
| `café` / `quantum entanglement` (unrelated) | 0.01 | 0.03 |

SK is `1.0000` because accent-folding makes the two inputs the *same* token sequence.
Unrelated text stays far apart (~0.02), so SK is not collapsing everything — it is doing
exactly the right thing. Under the hand-rolled path, a search for "Muller" would essentially
**not find** "Müller" (0.12 ≈ unrelated). This is the correctness gap the migration closes.

### 5. Input pre-normalization (`FormC`) does not help

A proposed fix was to `rawText.Normalize(NormalizationForm.FormC)` before SK. Measured — it
is a **no-op**: `cosine(SK(raw), SK(FormC)) = 1.0000` on every input, because FastBertTokenizer
re-normalizes internally regardless of input form. It also does **not** move the hand-rolled
path any closer (`cosine(HR, SK·FormC)` equals `cosine(HR, SK)` exactly). `FormC` *composes*
accents (keeps them); the model needs them *stripped* — the opposite direction. There is no
input massaging that reconciles the two; the fix is to use the correct tokenizer.

### 6. Why the hand-rolled tokenizer exists (context, inference)

This code originated in the archived **`kurrent-io/Kurrent.Kontext`** repo (which began life as
a standalone tool called `ksearch`), so KurrentDB's local git history is not its real provenance.
The tokenizer was born in the initial commit `a3454a142` (shaan1337, 2026-03-10,
Claude-co-authored) — *"Add ksearch: hybrid text + vector search over KurrentDB events"* — a fast,
AI-assisted drop of a whole RAG tool (ONNX `all-MiniLM-L6-v2` embeddings + OpenSearch BM25/kNN +
cross-encoder rerank), then perf-tuned in `5b4daa2bb` (2026-04-01: INT8 quantization, dynamic token
length, thread limiting). **No commit documents *why* the tokenizer was hand-rolled** instead of
taken from a library. Plausible reasons, as inference:

- **AOT / minimal dependencies.** The repo targets Native AOT and bans reflection. A
  dependency-free, span-based, zero-alloc tokenizer (tokenizer pooling, `GetAlternateLookup`,
  preallocated buffers) fits that ethos; pulling the whole Semantic Kernel stack (heavy,
  alpha, `[Experimental]`) into a database engine plugin is a real cost to avoid.
- **One tokenizer for two jobs.** `WordPieceTokenizer` also serves the **cross-encoder**
  reranker via `EncodePair(query, document)` (query/document pair encoding with
  `token_type_ids`). SK's `BertOnnx` connector is embedding-only and does not expose pair
  encoding, so a single hand-rolled tokenizer covered both embedding and reranking.
- **Performance intent.** The code is clearly tuned for throughput over a large event corpus.
- **English-only validation.** The tokenizer is *correct and identical to SK for English*
  (Finding 2); the accent gap only shows on non-ASCII input, which was likely never tested.

The takeaway is not "it was done badly" — it is that the hand-rolled path trades correctness
on non-English text for zero dependencies, and that trade is no longer necessary now that
SK's `BertOnnx` exposes a first-party `IEmbeddingGenerator` (subject to the AOT check below).

## Recommendations

1. **Adopt SK `BertOnnx` via `AddBertOnnxEmbeddingGenerator`** (returns
   `IEmbeddingGenerator<string, Embedding<float>>` directly; the `BertOnnxTextEmbeddingGenerationService`
   class is already `[Obsolete]` in favor of the extension). This lands the "every provider is
   a uniform `IEmbeddingGenerator` by convention" goal for free — no hand-written wrapper.
2. **Configure to match:** `NormalizeEmbeddings = true`, `CaseSensitive = false`,
   `PoolingMode = Mean` (default). These are what make it equivalent-or-better (Findings 2/4).
3. **Keep `ModelManager` for model *selection* only** (AVX-512/AVX-2 pick + load from the
   embedded models assembly). Feed the selected model+vocab **streams** into SK's `Create`/DI
   overload. SK does not do model selection.
4. **Delete `EmbeddingService`** — fully replaced.
5. **Do NOT blindly delete `WordPieceTokenizer` yet.** It is also used by
   `Search/CrossEncoderService` via `EncodePair`. SK's `BertOnnx` does not cover cross-encoder
   pair tokenization. Either keep `WordPieceTokenizer` scoped to the reranker, or migrate the
   reranker's tokenization separately. This must be part of the same issue.

### Open risks to close before committing

- **AOT (blocking).** The repo ships AOT-compatible. `Microsoft.SemanticKernel.Connectors.Onnx`
  + `FastBertTokenizer` AOT/trim compatibility is **unverified**. Run the AOT report against a
  project referencing them before adopting. This may be the very reason the path was hand-rolled.
- **Alpha / experimental dependency.** The connector is `1.45.0-alpha` and gated behind
  `SKEXP0070`. Weigh shipping an alpha SK dependency inside the DB engine.
- **Non-Latin scripts = model ceiling, not tokenizer (now a requirement).** SK *tokenizes*
  Cyrillic/CJK correctly, but `all-MiniLM-L6-v2` is an English uncased model with weak semantics
  there. Multilingual coverage is a **confirmed requirement** (English / Latin / Portuguese /
  Cyrillic), so a **model swap is in scope** — see the Multilingual section below. Critically,
  the model choice is coupled to the tokenizer choice and may make the SK `BertOnnx` path moot.
- **Accent-folding is intended.** `café` = `cafe` is correct for retrieval. If distinguishing
  accented forms is ever a requirement, that is a different design (and not what search wants).

## Multilingual requirement (English / Latin / Portuguese / Cyrillic)

Clarified after the analysis: Kontext must handle English, Latin/Romance (incl. Portuguese),
and **Cyrillic**. `all-MiniLM-L6-v2` is an English uncased model and is **insufficient** here —
no tokenizer change fixes that. So a **model swap is part of the migration**, and the model
choice is **coupled to the tokenizer**, which decides whether the SK `BertOnnx` path still applies:

| Model | ~Params | Dim | Tokenizer family | Coverage | Works with SK `BertOnnx`? |
|---|---|---|---|---|---|
| `intfloat/multilingual-e5-small` | 118M | 384 | XLM-RoBERTa (SentencePiece) | ~100 langs | ❌ needs a SentencePiece tokenizer |
| `paraphrase-multilingual-MiniLM-L12-v2` | 118M | 384 | XLM-RoBERTa (SentencePiece) | 50+ langs | ❌ needs a SentencePiece tokenizer |
| `distiluse-base-multilingual-cased-v2` | 135M | 512 | WordPiece (DistilmBERT) | 50+ langs | ✅ FastBertTokenizer / SK works |

- **Best quality, still small — `multilingual-e5-small`** (384-dim, so it keeps the current
  vector dimension). But it uses **XLM-R SentencePiece** tokenization, so **SK's `BertOnnx`
  connector does not apply** — it would need a SentencePiece tokenizer (e.g. `Microsoft.ML.Tokenizers`
  or BlingFire, to be verified for XLM-R) plus a thin ONNX inference wrapper. E5 expects
  `query:` / `passage:` prefixes, which map cleanly onto the existing `EmbeddingPurpose` split.
- **`distiluse-base-multilingual-cased-v2` — dropped.** It looked like a WordPiece drop-in, but it
  has a Dense 768→512 Tanh projection head that SK `BertOnnx` (transformer + pool + L2 only) does
  **not** apply — you'd get weaker raw 768-dim vectors, not real distiluse. Not a clean OOTB path.

**Empirical check (2026-07-11):** ran `multilingual-e5-small` via transformers.js (trusted
reference) on the target scripts. Accent-robust (São Paulo/Sao Paulo `0.98`, José/Jose `0.97`,
informação/informacao `0.94`) and — the real win — strong **cross-lingual** alignment
(Москва/Moscow `0.96`, Россия/Russia `0.96`, banco de dados/database `0.93`, gato/cat `0.92`,
Привет мир/Hello world `0.89`), with an unrelated pair clearly lower (Москва/quantum entanglement
`0.77`). **Caveat:** e5 has a high similarity *floor* (~`0.76` even for unrelated text) — absolute
cosines are compressed, so rely on relative ranking and model-tuned thresholds, not absolute
cutoffs. This confirms e5-small is fit for English / Latin / Portuguese / Cyrillic. **Still open:**
reproducing these vectors from a **C# XLM-R / SentencePiece** path (the actual product runtime).

**Decision:** best multilingual quality → E5-small + SentencePiece (more work; SK `BertOnnx`
becomes irrelevant). Keep the SK `BertOnnx` path → distiluse (WordPiece). All three cover
Portuguese and Cyrillic.

> The model facts above (params, dims, tokenizer family, ONNX availability) are from general
> knowledge; verify against the current HuggingFace model cards before finalizing the issue.

## Out-of-the-box tooling — can we avoid hand-rolling? (research 2026-07-11)

Two parallel research tracks (Semantic Kernel + ecosystem; `Microsoft.ML.Tokenizers`), each verified
by building and running probes, plus a baseline console app that runs the current Kontext path as-is
(`scratchpad/baseline/`, byte-identical code → `baseline-vectors.json`, 384-dim, café/cafe = 0.46).

**Verdict: no single turnkey package gives a local, in-process, AOT-safe, *multilingual*
`IEmbeddingGenerator` with zero glue. But `Microsoft.ML.Tokenizers` (GA, first-party, pure-managed)
means we never hand-roll a tokenizer again — it covers *both* tokenization jobs — leaving only a
~40-line ONNX inference wrapper (which already exists in `EmbeddingService`) as our code.**

| Option | Local / in-proc | Multilingual (e5/XLM-R) | Release | AOT | Glue we write |
|---|---|---|---|---|---|
| SK `BertOnnx` (`AddBertOnnxEmbeddingGenerator`) | ✅ | ❌ WordPiece only | alpha `SKEXP0070`; pulls SK.Core + OnnxRuntimeGenAI | worked-on | none (WordPiece models only) |
| **`Microsoft.ML.Tokenizers` `BertTokenizer`** + our ONNX wrapper | ✅ | mBERT WordPiece only | **GA 2.0.0, pure-managed** | empirically clean | ONNX pool/normalize (have it) |
| **`Microsoft.ML.Tokenizers` `SentencePieceTokenizer`** + our ONNX wrapper | ✅ | ✅ **e5 / XLM-R** | **GA 2.0.0** | empirically clean | + fairseq id offset & special-token remap |
| Ollama (`AddOllamaEmbeddingGenerator`) | ⚠️ external daemon | ✅ (bge-m3, arctic-embed2) | alpha | n/a (external) | none, but an ops dependency |
| Cloud (OpenAI/Azure/Google/Bedrock/Mistral) | ❌ remote | ✅ | GA pkg / alpha API | n/a | none, but network + API keys |
| `Tokenizers.DotNet` (loads HF `tokenizer.json`) | ✅ | ✅ | unofficial, 1 maintainer | native Rust, no AOT claim | ONNX wrapper |

**`Microsoft.ML.Tokenizers` — proven, not theorized** (a `PublishAot` native binary tokenized both correctly):
- **all-MiniLM (WordPiece):** `BertTokenizer.Create(vocab.txt, new BertOptions { LowerCaseBeforeTokenization = true, RemoveNonSpacingMarks = true, IndividuallyTokenizeCjk = true })` → verified `café → "cafe"`, `São Paulo → "sao paulo"`, no `[UNK]`. Fixes the accent bug.
- **e5-small (XLM-R Unigram):** `SentencePieceTokenizer.Create(sentencepiece.bpe.model)` → verified correct segmentation of `café`, `São Paulo`, `Москва`. Two fixes we write: **(A)** add the fairseq **+1 id offset** and inject e5's special tokens (`<s>`=0,`<pad>`=1,`</s>`=2,`<unk>`=3,`<mask>`=250001); **(B)** ship the `.model` protobuf — there is no HF `tokenizer.json` loader.
- **AOT:** not *declared* `IsAotCompatible`, but `PublishAot` produced **0 IL warnings** and a working native binary for both paths. Pin the version + keep a CI AOT gate.

**⚠️ Operational landmine — `InvariantGlobalization` silently reproduces the bug.** BERT accent-stripping calls `String.Normalize(FormD)`, which needs **ICU**. With `InvariantGlobalization=true` (set in several repo projects, incl. `KurrentDB.Kontext.Contracts.csproj`), `String.Normalize` is a **silent no-op — no exception** — so `café → [UNK]` again, the very bug we're fixing. A correct tokenizer is **necessary but not sufficient**: the shipping executable must run with **ICU present (`InvariantGlobalization=false`**, app-local ICU for Native AOT). Verify what the `KurrentDB` server resolves at runtime.

### Revised recommendation
1. **Tokenizer: adopt `Microsoft.ML.Tokenizers`** (GA, first-party, managed, AOT-clean) for both models — never hand-roll a tokenizer again. Keep the existing ~40-line ONNX inference (mean-pool + L2).
2. **Now (accent fix, low risk):** replace the hand-rolled `WordPieceTokenizer` with `BertTokenizer` on the current all-MiniLM model.
3. **Multilingual (the real goal):** `multilingual-e5-small` + `SentencePieceTokenizer` + the fairseq remap + ONNX wrapper. Best quality; most AOT-safe managed footprint; no native/alpha deps.
4. **Gate on ICU (`InvariantGlobalization=false`) in the shipping binary**, plus a CI Native-AOT publish check.
5. SK `BertOnnx` only if a zero-inference-code path is required AND you stay WordPiece — it's alpha and can't do e5, so it is *not* the multilingual answer.

## Head-to-head: baseline vs tokenizer-fix vs e5-small (2026-07-11, verified)

One console app (`scratchpad/comparison/`) over a shared 26-input multilingual set, three impls:
**A** = existing hand-rolled (WordPiece + all-MiniLM, the incumbent); **B** = all-MiniLM +
`Microsoft.ML.Tokenizers` `BertTokenizer` (correct tokenizer, same ONNX); **C** =
`multilingual-e5-small` (C#: `SentencePieceTokenizer` + fairseq remap + ONNX).

| Pair | Kind | A (existing) | B (MiniLM + ML.Tok) | C (e5-small) |
|---|---|---|---|---|
| café / cafe | accent | 0.46 | **1.00** | 0.99 |
| José / Jose | accent | 0.40 | **1.00** | 0.97 |
| naïve / naive | accent | 0.22 | **1.00** | 0.97 |
| São Paulo / Sao Paulo | PT accent | 0.68 | **1.00** | 0.98 |
| informação / informacao | PT accent | 0.67 | **1.00** | 0.94 |
| cão / dog | PT↔EN | 0.28 | 0.26 | **0.92** |
| 東京 / Tokyo | JA↔EN | 0.47 | 0.66 | **0.94** |
| こんにちは世界 / Hello world | JA↔EN | 0.09 | 0.18 | **0.88** |
| 日本語 / Japanese language | JA↔EN | 0.32 | 0.63 | **0.89** |
| Москва / Moscow | RU↔EN | 0.55 | 0.55 | **0.96** |
| Россия / Russia | RU↔EN | 0.54 | 0.54 | **0.96** |
| Привет мир / Hello world | RU↔EN | 0.14 | 0.14 | **0.89** |
| Москва / quantum entanglement | FAR | −0.01 | −0.01 | 0.77 |

**Verification:**
- **C is bit-exact to the transformers.js e5 reference — min cosine `1.000000` across all 26 inputs**, correct on the first run. The C# XLM-R path (fairseq remap `hf_id = sp_id==unk ? 3 : sp_id+1`, `<s>…</s>` wrap, `input_ids`+`attention_mask` only — e5 declares no `token_type_ids` — `"query: "` prefix) is proven correct. **The multilingual C# path works.**
- **B fixes the accent bug** (1.00 vs A's 0.22–0.68) and **reproduces A bit-for-bit on ASCII** (cosine `1.000000`). B is also effectively the **"existing ONNX shell + a correct tokenizer" backup**.
- **ICU gate enforced:** the app asserts `StripAccents("café")=="cafe"` at startup and throws under invariant globalization.

**Reading it:** A is English-only and collapses on accents/CJK/Cyrillic. B fixes accents for free but is still an English model — weak on Japanese/Cyrillic *concepts* (and identical to A on Cyrillic, which has no accents to fold). C handles all of it. Note e5's compressed range (FAR = 0.77) — use relative/model-tuned thresholds, not absolute cutoffs.

**Int8 quantized e5 (Path D) — ship this variant.** The `model_quantized.onnx` (118 MB vs the fp32 470 MB, ~4× smaller) tracks fp32 almost exactly: **quantization drift cosine(C fp32, D int8) min `0.9945` / mean `0.9961`** across the 26 inputs, and every pair in the table stays within ~0.01 of fp32 (accent-robustness and cross-lingual alignment fully preserved). So the ~0.4% mean vector shift buys a 4× smaller model — adopt the **int8** e5 for the real implementation; the fp32 model is only needed as the bit-verify ground truth. (This run also exercised a real from-scratch download of the 118 MB file, so the download path is now validated end-to-end, not just by inspection.)

## Model distribution — how B/C get their models (A's `Models` project stays as-is)

The baseline (A) embeds the ONNX into a netstandard2.0 DLL: build-time `DownloadFile` from
HuggingFace → `EmbeddedResource` → runtime `Assembly.Load` + `GetManifestResourceStream`. Fine to
leave as the baseline, but prototype-grade for new code — it bloats the binary, needs network at
*build*, has no integrity/version check, can't update a model without a rebuild, and `Assembly.Load(string)`
is an AOT smell. For B/C, treat the model as a **runtime asset**, not a baked-in resource. Options:

| # | Approach | How | Pros | Cons |
|---|---|---|---|---|
| 1 | Runtime download → cache, SHA-256 + pinned revision | first use fetches to `{db-data}/kontext/models/`, verify hash, load from disk | lean binary; updatable; integrity-checked; no reflection; fine for the 118 MB e5 | first-run network (mitigate via pre-seed) |
| 2 | Operator-provided path (config) | a setting points at a models dir the operator provisions (manual / init-container / sidecar) | cleanest for on-prem/air-gap; engine downloads nothing; ops owns versions | worse out-of-box DX; needs docs |
| 3 | Versioned NuGet package of models | ship `.onnx` as package *content* (not embedded-in-code), restored + hash-verified by NuGet | versioned; integrity via package hash; no build-time HF call | large package to host/publish |
| 4 | OCI artifact / object store | pull from an internal registry or S3-like bucket, content-addressed | enterprise distribution + caching | infra dependency; overkill unless already run |

**Recommendation:** models are a **runtime asset, never baked into the binary.** Make **#2 the
contract** (a configurable models directory) and **#1 the convenience default** (download-to-cache
with a pinned SHA + revision for connected installs), and **always support pre-seeding** that
directory for air-gapped deployments. This removes the binary bloat, the build-time network, and the
`Assembly.Load` reflection, and gives integrity + updatability. The int8 e5 (118 MB, near-lossless —
see head-to-head) makes this materially easier than the fp32 470 MB model.

## Method

- **Harness:** a throwaway file-based C# app (`scratchpad/compare.cs`) feeding the **same**
  `embedding/model_quint8_avx2.onnx` + `vocab.txt` (from `KurrentDB.Kontext.Models`) to both paths,
  so the only variables are tokenization, pooling, and normalization.
- **Path A (hand-rolled):** a **verbatim copy** of `WordPieceTokenizer.cs` plus the exact
  `EmbeddingService.GetEmbedding` logic (mean-pool over attention mask + L2 normalize).
- **Path B (SK):** `BertOnnxTextEmbeddingGenerationService` (`Microsoft.SemanticKernel.Connectors.Onnx`,
  latest prerelease), `PoolingMode = Mean`, `NormalizeEmbeddings = true`, `CaseSensitive = false`.
- **Metrics:** cosine similarity and element-wise max absolute difference over the 384-dim vectors.
- **Environment:** .NET SDK 11.0.100-preview, Apple Silicon (arm64) — so `ModelManager`'s
  selection resolves to the `model_quint8_avx2.onnx` variant; both paths used that exact file.
- **Not covered:** multi-item batches / padding behavior (tested single-item only), non-Latin
  *semantic* quality, AOT publish, and throughput/allocation benchmarks.
