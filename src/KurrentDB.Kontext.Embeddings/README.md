# KurrentDB.Kontext.Embeddings

**Text-embedding generators** for Kontext (KurrentDB's agent-memory / RAG layer). Two families live here:
**local, on-CPU ONNX generators** that turn a string into a dense float vector in-process via
[ONNX Runtime](https://onnxruntime.ai/) — no external service — and thin **DI adapters for remote / hosted
embedding services** (OpenAI, Ollama, Google Vertex AI, Amazon Bedrock) that call a provider over the network.
Both sit behind one interface, so the rest of Kontext never learns which is in use.

Every generator is a standalone `Microsoft.Extensions.AI`
`IEmbeddingGenerator<string, Embedding<float>>` — the same shape as Microsoft's own `OpenAIEmbeddingGenerator`.
You construct it, register it with `AddEmbeddingGenerator`, and it composes with vector stores and the
Microsoft.Extensions.AI middleware pipeline. Consumers depend on the interface, not on any specific model or
tokenizer.

On the **local ONNX** side, the project holds **four generators that tell a "prototype evolution" story** over
the same all-MiniLM model, plus one multilingual generator on a different model lineage (the network-backed
adapters are covered in [Remote / hosted providers](#remote--hosted-providers)):

| Folder | Model | Tokenizer | Dims | Provider id | Status |
|--------|-------|-----------|------|-------------|--------|
| `Prototype/` | all-MiniLM-L6-v2 | hand-rolled WordPiece | 384 | `local` | **frozen default** — what existing indexes were built with |
| `PrototypeV2/` | all-MiniLM-L6-v2 | hand-rolled WordPiece | 384 | `kontext-prototype-v2` | clean re-implementation of Prototype, **byte-identical** to it |
| `WordPieceOnnx/` | all-MiniLM-L6-v2 | `BertTokenizer` (correct) | 384 | `kontext-wordpiece-onnx` | opt-in — **accent bug fixed** (different vectors) |
| `SentencePieceOnnx/` | any XLM-R model (e5, paraphrase-multilingual, bge-m3) | SentencePiece + fairseq remap | model-dependent (384–1024) | `kontext-sentencepiece-onnx` | **multilingual** |

> `Prototype/` and `PrototypeV2/` — the former **A** and **A′** in the prototype-evolution story — are the
> **frozen prototype showcase**: same model, cleaner architecture, kept byte-faithful on purpose.
> `WordPieceOnnx/` and `SentencePieceOnnx/` — the former **B** and **C** — are the **two real generators**,
> named by tokenizer family (WordPiece vs SentencePiece) rather than by version. SentencePieceOnnx is a
> *different* model family (multilingual), so it is **not** named "V4" — that would imply it supersedes
> WordPieceOnnx, which it does not.

---

## How it works

Adding a model family means **adding a generator class**, not configuring a generic engine. The three ONNX
generators (PrototypeV2, WordPieceOnnx, SentencePieceOnnx) are independent `IEmbeddingGenerator`
implementations that share only the plumbing at the project root; they differ solely in the tokenizer each one
holds:

- **`InferenceSessionExtensions.cs`** — the shared, tokenizer-agnostic ONNX math, exposed as an
  `InferenceSession.Embed(…)` extension (`internal`). Given a tokenized input it builds the ONNX inputs
  (`input_ids`, `attention_mask`, and `token_type_ids` **only when the model declares that input**) → runs
  the session → **pools** the per-token outputs into one vector → optionally **L2-normalizes**. This is the
  identical math every generator runs via `session.Embed(…)`.
- **`EncodedInput.cs`** — the `internal` carrier each generator's private `Encode` produces and passes to
  `session.Embed`: `InputIds`, `AttentionMask`, and optional `TokenTypeIds`.
- **`EmbeddingPoolingMode.cs`** — `Mean` (average over the attention mask; the sentence-transformers default,
  used by all-MiniLM and multilingual-e5) or `Cls` (first-token vector; used by models trained for it, e.g.
  bge-m3).
- **`OnnxModelLoader.cs`** — helpers to turn model bytes into an `InferenceSession` (`ORT_SEQUENTIAL`).
- **`OnnxModel.cs`** — a lazy handle to one model's files: `ReadModel()` (first-class) + `ReadAsset(name)`
  for companion files (tokenizer vocabs, SentencePiece models). Nothing is read until a generator asks; build
  one with `FromFiles(...)`, `FromManifest(...)`, or `FromEmbeddedResources(...)` — the last reads the model +
  assets from an assembly's manifest resources (AOT-safe, no `Assembly.Load`; the interim bridge — see
  [Interim model loading](#interim-model-loading-embedded-bridge)). Used by **both WordPieceOnnx and
  SentencePieceOnnx**; PrototypeV2 still takes a `Stream`.
- **`OnnxModelManifest.cs`** — declarative per-model config (`Key`, `Model` file, `RepoUrl`, `Assets`) — one
  shape shared by the registry, the generators, and the (future) downloader.
- **`OnnxModelRegistry.cs`** — resolves a model key → lazy `OnnxModel` from registered manifests (`Get` is
  cheap; bytes move only on read). Register models in code, or bind them from configuration via
  `services.AddOnnxModelRegistry(section)` — source-generated, reflection-free.

DI registration is **not** a root file — each generator ships its own `…ServiceCollectionExtensions` in its
own folder (see [Registering a generator](#registering-a-generator-di)).

There is no bespoke tokenizer type. **WordPieceOnnx and SentencePieceOnnx each hold a `Microsoft.ML.Tokenizers`
tokenizer directly** (`BertTokenizer` / `SentencePieceTokenizer`) and do their own text → `EncodedInput`
encoding inline in a private `Encode` — building the attention mask, and for SentencePieceOnnx the XLM-R
fairseq id remap. That encoding is deliberately **not** a `Tokenizer` subclass: `Tokenizer`'s contract is
text↔ids (it returns ids only — no attention mask, no `token_type_ids`), so assembling the ONNX input is the
generator's job, layered *on top of* the canonical tokenizer. (PrototypeV2, frozen, keeps its own hand-rolled
`HandRolledWordPieceTokenizer`.) Each generator's constructor does a one-off **warm-up probe**
(`session.Embed(Encode("probe"), …)`) so the reported embedding dimension is read from the model's real output
rather than hard-coded.

---

## The folders

### `Prototype/` — the frozen V1 default

The **original** hand-rolled embedding code, moved verbatim from `KurrentDB.Kontext` (git history preserved).
This is the path the Kontext core still wires today and the vectors already in existing indexes were produced
by it, so **its behaviour must not change**.

- **`EmbeddingService.cs`** — `IEmbeddingGenerator` over all-MiniLM-L6-v2. Note it runs its **own** inline
  inference + mean-pool + L2 loop (it predates the shared inference helper and does not use it) and pools a
  fixed 384 dims. Rents tokenizers from a 64-slot pool for concurrency. `GetEmbedding` is `internal` and
  exercised by the Kontext test suites (`InternalsVisibleTo`).
- **`WordPieceTokenizer.cs`** — the hand-rolled WordPiece tokenizer. Has both `Encode` (single sentence) and
  `EncodePair` (query/document, used by the cross-encoder reranker). Carries the known tokenizer bugs (see
  [Known issues](#known-issues--caveats)) on purpose.
- **`ModelManager.cs`** — loads the ONNX models **embedded in the `KurrentDB.Kontext.Models` assembly** via
  `Assembly.Load` + manifest resources, picking an AVX-512 or AVX-2 INT8 variant by CPU feature.
- **`PrototypeServiceCollectionExtensions.cs`** — `AddLocalEmbeddings()`, the `local` provider's DI helper. It
  registers the concrete `EmbeddingService` **and** the `IEmbeddingGenerator` interface pointing at it, so the
  Kontext startup can preload the model by resolving `EmbeddingService` directly.
- **`LocalEmbeddingsOptions.cs`** — `BatchSize` (1); bound as `KontextEmbeddingsConfig.Local`.

### `PrototypeV2/` — Prototype re-expressed as a standalone generator

Prototype's exact behaviour re-built as a clean, self-contained `IEmbeddingGenerator` — **verified
byte-identical to Prototype** (max abs diff 0.0). It exists to prove the refactored architecture reproduces the
original bit-for-bit; it is a showcase, not a behaviour change.

- **`PrototypeV2EmbeddingGenerator.cs`** — the generator class; construct with
  `new PrototypeV2EmbeddingGenerator(model, vocab, options)`. The shipped **default** choice.
- **`PrototypeV2Options.cs`** — `PoolingMode` (Mean), `NormalizeEmbeddings` (true), `MaxTokens` (512),
  `ModelId`. Deliberately **no** case/normalization knob — its output must never drift from what is indexed.
- **`HandRolledWordPieceTokenizer.cs`** — the verbatim hand-rolled tokenization logic (single-sentence only).
  Its shared per-instance buffers are not re-entrant, so it's `lock`-guarded around the (fast) tokenization;
  ONNX inference still runs unserialized. This is the one place a tokenizer stays a separate class — PrototypeV2
  is frozen, so it is left exactly as ported.
- **`PrototypeV2ServiceCollectionExtensions.cs`** — the `AddPrototypeV2Embeddings(…)` DI helper.

### `WordPieceOnnx/` — the tokenizer fix

The **same all-MiniLM model** as Prototype, but with a correct, maintained tokenizer. Bit-identical to
Prototype on plain ASCII; it correctly **folds accents** (`café` ≡ `cafe`) instead of mangling them. Opt-in,
because it produces different vectors from Prototype for accented / non-Latin text.

- **`WordPieceOnnxEmbeddingGenerator.cs`** — the generator class; two constructors mirroring SentencePieceOnnx:
  `(OnnxModelRegistry registry, options)` (main — resolves the model named by `options.ModelId`) and
  `(OnnxModel model, options)` (direct, for tests). Reads the ONNX model + WordPiece vocab asset from the
  `OnnxModel`, holds a `Microsoft.ML.Tokenizers.BertTokenizer` (uncased-BERT preprocessing: lower-case, strip
  accents, per-character CJK splitting) and encodes inline. Its constructor runs a **fail-loud ICU gate** —
  asserts `café` and `cafe` tokenize identically and throws otherwise, so the accent fix can't silently no-op
  (see issue #5 below).
- **`WordPieceOnnxOptions.cs`** — same shape as `PrototypeV2Options`, plus `VocabAsset` (the `OnnxModel` asset
  name for the vocab file; defaults to `vocab.txt`) — the WordPiece analogue of SentencePieceOnnx's
  `TokenizerAsset`.
- **`WordPieceOnnxServiceCollectionExtensions.cs`** — the `AddWordPieceOnnxEmbeddings(…)` DI helper; resolves
  the model from the `OnnxModelRegistry` in DI, keyed by `ModelId` (same shape as SentencePieceOnnx).

### `SentencePieceOnnx/` — multilingual

Runs **any XLM-RoBERTa-tokenized ONNX model** (multilingual-e5, paraphrase-multilingual, bge-m3). Switching
between those models is a **model-asset swap with zero code change** — only options differ. The C# path is
verified **bit-exact against a transformers.js reference** for `multilingual-e5-small`.

- **`SentencePieceOnnxEmbeddingGenerator.cs`** — the generator class; two constructors:
  `(OnnxModelRegistry registry, options)` (main — resolves the model named by `options.ModelId`) and
  `(OnnxModel model, options)` (direct, for tests). Reads the ONNX model + SentencePiece asset from the
  `OnnxModel`, holds a
  `Microsoft.ML.Tokenizers.SentencePieceTokenizer`, and applies the fairseq id remap XLM-R ONNX models expect
  inline (shift ids +1, unknown → 3, wrap `<s>`…`</s>`). The package ships no XLM-R tokenizer, so that remap
  lives here.
- **`SentencePieceOnnxOptions.cs`** — adds `InputPrefix` (e5 requires `"query: "` / `"passage: "`;
  paraphrase-multilingual and bge-m3 use none) and lets you set `PoolingMode = Cls` for bge-m3.
- **`SentencePieceOnnxServiceCollectionExtensions.cs`** — the `AddSentencePieceOnnxEmbeddings(…)` DI helper.

---

## Remote / hosted providers

Alongside the local ONNX generators, this project hosts thin **DI adapters for hosted embedding services** —
one folder per source — so a deployment can swap in a managed provider without the rest of Kontext changing.
Each adapter is a plain `IEmbeddingGenerator<string, Embedding<float>>`; consumers never learn which provider
is in use.

Every source ships the same pair:

- **`<Source>EmbeddingsOptions`** — a settable-property options **class** (a class, not a record, so it binds
  through the configuration binding generator) holding the provider's endpoint / model / credentials and a
  `BatchSize`. Bind it straight from config with `section.Get<…Options>()` — no bespoke factory needed.
- **`<Source>EmbeddingsServiceCollectionExtensions`** — two `Add<Source>Embeddings` overloads on
  `IServiceCollection` (one taking the options, one taking `Action<…Options>`), each returning the
  `EmbeddingGeneratorBuilder<string, Embedding<float>>` for middleware chaining. Validation and client
  construction run **lazily inside the DI factory**, so a missing key / region surfaces when the generator is
  resolved, not at registration.

| Folder | Provider | Client | Requires |
|--------|----------|--------|----------|
| `OpenAI/` | OpenAI (and OpenAI-compatible proxies) | `OpenAIClient.GetEmbeddingClient().AsIEmbeddingGenerator()` | `ApiKey` (`Endpoint` optional, for proxies) |
| `Ollama/` | Ollama | `OllamaApiClient` (is itself an `IEmbeddingGenerator`) | — (defaults to `localhost:11434` / `nomic-embed-text`) |
| `GoogleVertexAI/` | Google Vertex AI | `PredictionServiceClientBuilder.BuildIEmbeddingGenerator()` | `ProjectId` + `Region` |
| `Aws/` | Amazon Bedrock | `AmazonBedrockRuntimeClient.AsIEmbeddingGenerator()` (Titan) or `CohereBedrockEmbeddingGenerator` (`cohere.*`) | `Region` |

```csharp
// From explicit settings…
services.AddOpenAIEmbeddings(new OpenAIEmbeddingsOptions { ApiKey = "sk-…" });

// …configured inline…
services.AddOllamaEmbeddings(o => { o.Endpoint = "http://ollama:11434"; o.Model = "nomic-embed-text"; });

// …or bound from configuration via the standard binder.
services.AddAmazonBedrockEmbeddings(
    config.GetSection("Embeddings:AmazonBedrock").Get<AmazonBedrockEmbeddingsOptions>() ?? new());
```

`Aws/` keeps a dedicated **`CohereBedrockEmbeddingGenerator`** because the AWS MEAI extension marshals Titan's
request format only; `cohere.*` models are routed to it automatically. **`EmbeddingPurpose`** (project root)
carries the document-vs-query intent those asymmetric Cohere models need — read from
`EmbeddingGenerationOptions`; generators that don't distinguish ignore it.

The Kontext composition root selects one provider from `KontextEmbeddingsConfig.Provider` and calls the
matching `Add…Embeddings` helper; the `local` provider maps to the on-CPU Prototype generator via
`AddLocalEmbeddings()`.

---

## Registering a generator (DI)

Because each generator is a plain `IEmbeddingGenerator`, you register it the canonical Microsoft.Extensions.AI
way — `services.AddEmbeddingGenerator(sp => new …EmbeddingGenerator(…))` — which returns an
`EmbeddingGeneratorBuilder<string, Embedding<float>>` you can chain middleware onto (`.UseLogging()`,
`.UseOpenTelemetry()`, …). Each generator ships its own thin helper in its folder
(`PrototypeV2ServiceCollectionExtensions`, `WordPieceOnnxServiceCollectionExtensions`,
`SentencePieceOnnxServiceCollectionExtensions`). Model **distribution stays decoupled from the generators**:
**WordPieceOnnx and SentencePieceOnnx** resolve an `OnnxModel` from the `OnnxModelRegistry` in DI (keyed by
`ModelId`), so *where the bytes live* is the registry's concern; **PrototypeV2** still takes a `modelSource`
delegate returning the model + vocab streams. Either way this project says nothing about how a model got onto
disk.

```csharp
// PrototypeV2 — the byte-faithful default (still takes model + vocab streams directly)
services.AddPrototypeV2Embeddings(sp => (OpenModelStream(), OpenVocabStream()));

// WordPieceOnnx and SentencePieceOnnx both resolve their model from an OnnxModelRegistry in DI. Bind the
// registry from config once (a "ModelsDirectory" + a "Models" manifest array), then select each model by key:
services.AddOnnxModelRegistry(config.GetSection("Kontext:Embeddings"));

// WordPieceOnnx — same all-MiniLM model as Prototype, correct tokenizer (requires ICU; see issue #5)
services.AddWordPieceOnnxEmbeddings(o => o.ModelId = "all-MiniLM-L6-v2");

// SentencePieceOnnx — multilingual; e5 needs the "query: " prefix
services.AddSentencePieceOnnxEmbeddings(o => { o.ModelId = "multilingual-e5-small"; o.InputPrefix = "query: "; });

// …or register any generator directly over a resolved OnnxModel (what the tests and the interim bridge do):
services.AddEmbeddingGenerator(sp => new WordPieceOnnxEmbeddingGenerator(model));
```

The original **Prototype** (`EmbeddingService`) now has its own `AddLocalEmbeddings()` helper in `Prototype/`
(the `local` provider) — it registers both the concrete `EmbeddingService` and the interface, so Kontext
startup can preload the model. See [Remote / hosted providers](#remote--hosted-providers) for how the
composition root picks a provider.

---

## Interim model loading (embedded bridge)

Until the `HuggingFace.ModelDownloader` lands, the interim production model ships **embedded in the
`KurrentDB.Kontext.Models` assembly** and is loaded with no on-disk cache and no reflection:

- **`OnnxModel.FromEmbeddedResources(name, assembly, modelResource, assetResources)`** reads the model + its
  companion assets from an assembly's manifest resources via `Assembly.GetManifestResourceStream` — **AOT-safe,
  no `Assembly.Load`**. It is generic over the assembly, so the SDK stays decoupled from where the models live.
- **`KontextModelsAssembly`** — a one-line public marker in `KurrentDB.Kontext.Models` so callers reach that
  assembly reflection-free via `typeof(KontextModelsAssembly).Assembly`. The project's build target downloads
  and embeds the interim multilingual model **paraphrase-multilingual-MiniLM-L12-v2 (pMM12)**
  (`pmm12.model.onnx` + the shared XLM-R `pmm12.sentencepiece.bpe.model`) at build time — **not committed to
  git**, exactly like the existing all-MiniLM embed.
- **`services.AddInterimPmm12Embeddings()`** is the interim composition-root wiring: it builds an `OnnxModel`
  over those embedded resources and registers pMM12 through the SentencePieceOnnx generator, no input prefix.

This is deliberately a bridge. When the downloader lands, point `AddOnnxModelRegistry` at its populated cache
directory, switch the generators to the `(OnnxModelRegistry, options)` constructor, and delete the embed
target, `FromEmbeddedResources`, `KontextModelsAssembly`, and `AddInterimPmm12Embeddings` — only the *acquire*
side changes. Full spec:
[`docs/designs/2026-07-14-onnx-model-loading-bridge`](../../.claude/context/docs/designs/2026-07-14-onnx-model-loading-bridge/design.md).

---

## Known issues & caveats

Most of these live in the **frozen baseline (Prototype)** and are inherited by PrototypeV2 **on purpose**
(byte-faithfulness); WordPieceOnnx and SentencePieceOnnx are the fixes. The critical property is that several
baseline failures are **silent** — no exception, just quietly worse results. Full analysis:
[`docs/reports/2026-07-11-kontext-embeddings-sk-migration`](../../.claude/context/docs/reports/2026-07-11-kontext-embeddings-sk-migration/report.md).

| # | Issue | Affects | Severity | Fixed by |
|---|-------|---------|----------|----------|
| 1 | **Accents silently mangled** — `café` isn't normalized, shatters into `[UNK]`; `café`↔`cafe` cosine ≈ 0.46 | Prototype, PrototypeV2 | High | **WordPieceOnnx** |
| 2 | **English-only model** — all-MiniLM has weak non-English semantics *even with a perfect tokenizer* (PT/ES/FR within-language separation near zero) | Prototype, PrototypeV2, WordPieceOnnx | High | **SentencePieceOnnx** |
| 3 | **Non-Latin `[UNK]`-collapse → fake matches** — distinct CJK inputs collapse to the same `[UNK]` sequence, cosine 1.0 | Prototype, PrototypeV2 | Med-High | WordPieceOnnx, SentencePieceOnnx |
| 4 | **Hand-rolled tokenizer diverges from standard BERT** (case-insensitive lookups, no normalization, `char.IsPunctuation`, 200-char word cap) | Prototype, PrototypeV2 | Med | WordPieceOnnx, SentencePieceOnnx |
| 5 | **ICU landmine** — accent-folding needs `String.Normalize(FormD)`, a **silent no-op** under `InvariantGlobalization=true` or on ICU-less images (Alpine/musl); the fix silently reverts | WordPieceOnnx, SentencePieceOnnx | High | **fail-loud gate in WordPieceOnnx**; run with ICU (`InvariantGlobalization=false`) |
| 6 | **AVX variant selection is x86-centric** — `ModelManager` ships avx512 + avx2 copies and on ARM silently loads the one literally named `avx2` | Prototype | Low-Med | open (baseline kept) |
| 7 | **`Assembly.Load` reflection** in `ModelManager` — AOT-hostile in a repo targeting Native AOT | Prototype | Med | open (baseline kept; PrototypeV2 takes streams, WordPieceOnnx/SentencePieceOnnx resolve an `OnnxModel`, and the interim bridge reads embedded resources via `FromEmbeddedResources` — no `Assembly.Load`) |
| 8 | **Model distribution is prototype-grade** — Prototype's models are downloaded at *build* time and embedded in a DLL (~53 MB), no checksum, no versioning | Prototype | Med | open design decision; PrototypeV2 takes streams and WordPieceOnnx/SentencePieceOnnx resolve an `OnnxModel`, deferring the choice to the host (the interim pMM12 embed re-uses the same build-time mechanism, by design, until the downloader lands) |

**Structural notes (not behavioural bugs):**

- `PrototypeV2/HandRolledWordPieceTokenizer.cs` is an intentional **duplicate** of the tokenization logic in
  `Prototype/WordPieceTokenizer.cs` — kept so PrototypeV2 is a self-contained showcase.
- The **cross-encoder reranker** (`CrossEncoderService`, in `KurrentDB.Kontext`, not this project) still uses
  the hand-rolled `WordPieceTokenizer.EncodePair`, so the tokenizer bugs also degrade *reranking* for
  non-English content. It should be migrated to a correct tokenizer alongside any embedding change.

**Model-choice caveat:** the current multilingual recommendation (`paraphrase-multilingual-MiniLM-L12-v2` —
384-dim, same index width as Prototype/WordPieceOnnx) comes from a **synthetic word-pair** eval, not real
retrieval. A sentence-level recall@k eval on representative Kontext content should confirm before locking it in.

---

## Further reading

- [`.claude/context/project/kontext-embeddings-issues.md`](../../.claude/context/project/kontext-embeddings-issues.md) — the issues above, junior-friendly.
- [`.claude/context/project/kontext-retrieval-pipeline.md`](../../.claude/context/project/kontext-retrieval-pipeline.md) — how the embedding fits the full retrieval pipeline (BM25 + kNN → RRF → cross-encoder → MMR).
- [`.claude/context/docs/reports/2026-07-11-kontext-embeddings-sk-migration/report.md`](../../.claude/context/docs/reports/2026-07-11-kontext-embeddings-sk-migration/report.md) — the master analysis: accent bug, tooling, head-to-head model tables, quantization, distribution.
- **`KurrentDB.Kontext.Embeddings.Playground`** — a console harness that runs Prototype/WordPieceOnnx/SentencePieceOnnx plus downloaded multilingual models through the same pairs and prints ranking tables.
