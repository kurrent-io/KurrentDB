# Handoff: Configure and benchmark bge-m3 as a Kontext embedding model

## Session Metadata
- Created: 2026-08-17 00:05:52
- Project: /Users/sergio/dev/kurrent/kurrentdb
- Branch: kontext-reloaded
- Session duration: ~2 days across 2026-08-15 → 2026-08-17 (retrieval pipeline + FTS root-cause + normalizer arc)

### Recent Commits (for context)
  - f124461c1 refactor(kontext): unseal the onnx embedding generators - model-specific subclasses get a door
  - c6cb9594c docs(kontext): session memory - result tables sort by effectiveness, winner takes the trophy
  - b9ef17bf0 feat(kontext): byte-native JsonNormalizer - the embedding rendering that measured best
  - ddd2c6dae fix(kontext): pMM12 runs at its trained window - 128 tokens, not the positional ceiling
  - 86da0bd68 docs(kontext): session memory - the linear commit-ref closing hazard

## Handoff Chain

- **Continues from**: None (fresh start)
- **Supersedes**: None

> This is the first handoff for this task.

## Current State Summary

The Kontext retrieval work settled the shipped `Focused` chain (recall@5 0.4889) and fully
probed the embedding stack. The interim model pMM12 (paraphrase-multilingual-MiniLM-L12-v2,
384-dim, 128-token trained window, now correctly pinned) is the measured baseline. The agreed
next experiment: bring up **bge-m3** (XLM-R family, 1024-dim, 8192-token window, Cls pooling)
through the existing `SentencePieceOnnxEmbeddingGenerator` — the generator was BUILT for this
family and its options doc names bge-m3 explicitly — then benchmark it against pMM12 on the
LoCoMo ranking corpus and the JSON-rendering probe. Nothing bge-m3-specific exists in the repo
yet; this session starts it.

## Codebase Understanding

### Architecture Overview

- Embeddings: `Kurrent.Kontext.Embeddings` — `SentencePieceOnnxEmbeddingGenerator` (now
  UNSEALED, subclassing allowed by Sérgio's ruling) runs any XLM-R/SentencePiece ONNX model.
  Its knobs live in `SentencePieceOnnxOptions`: `PoolingMode` (bge-m3 = `Cls`, pMM12 = `Mean`),
  `NormalizeEmbeddings` (true — makes the store's `l2` metric equal cosine), `MaxTokens`
  (SET TO THE MODEL'S TRAINED WINDOW: bge-m3 = 8192, pMM12 = 128), `InputPrefix`
  (null for both; only e5 needs it), `TokenizerAsset`.
- Model loading: `OnnxModel` (see `OnnxModel.cs` — check its file-based constructors;
  `FromEmbeddedResources` is how pMM12 ships inside `KurrentDB.Kontext.Models`), plus
  `OnnxModelRegistry`/`OnnxModelManifest` for named registration. A model DOWNLOADER is
  planned but does not exist (`InterimPmm12.cs` doc: "goes when the downloader lands").
- Ranking measurement: `KontextCorpus` (419 memories / 150 questions, LoCoMo) over
  `KontextStoreFixture`; both take an OPTIONS seam (`Action<SentencePieceOnnxOptions>?`) but
  are HARDCODED to `InterimPmm12.CreateEmbeddingGenerator` — a MODEL seam (generator factory)
  must be added for bge-m3 (see Immediate Next Steps).
- Benchmarks: `Kurrent.Kontext.Benchmarks/Program.cs` has three modes — default (hill-climb),
  `--determinism`, `--max-tokens-ab` (COPY THIS PATTERN for a `--model-ab` mode).
- Normalization: `JsonNormalizer.Instance : IUtf8Normalizer` renders JSON for embedding
  (`key: value,` per line, split lowercase keys, unquoted values). With a code-aware model
  like bge-m3 the hypothesis is that raw JSON embeds fine WITHOUT it — that is one of the
  things to measure.

### Critical Files

| File | Purpose | Relevance |
|------|---------|-----------|
| `src/Kontext/Kurrent.Kontext.Embeddings/SentencePieceOnnx/SentencePieceOnnxEmbeddingGenerator.cs` | the generator bge-m3 runs through | unsealed; verify Cls pooling path |
| `src/Kontext/Kurrent.Kontext.Embeddings/SentencePieceOnnx/SentencePieceOnnxOptions.cs` | all the knobs | PoolingMode=Cls, MaxTokens=8192 for bge-m3 |
| `src/Kontext/Kurrent.Kontext.Embeddings/InterimPmm12.cs` | the pMM12 "one definition" — the pattern to mirror for a bge-m3 factory | also documents the embedded-resource loading |
| `src/Kontext/Kurrent.Kontext.Embeddings/OnnxModel.cs` + `OnnxModelRegistry.cs` | model asset loading | READ FIRST — pick file-based or registry path for a 570MB artifact |
| `src/Kontext/Kurrent.Kontext.Testing/KontextStoreFixture.cs` + `Corpus/KontextCorpus.cs` | corpus harness | needs the generator-factory seam |
| `src/Kontext/Kurrent.Kontext.Benchmarks/Program.cs` | benchmark modes | add `--model-ab` mirroring `--max-tokens-ab` |
| `src/Kontext/Kurrent.Kontext/KontextSchema.cs` | `KontextSchemaTask.Dimension = 384` | bge-m3 is 1024 — see Gotchas |
| `scripts/testing/test-runner.cs` | ALL test runs go through this | `dotnet scripts/testing/test-runner.cs -- run unit --treenode-filter ... --run-id <fresh guid>` |

### Key Patterns Discovered

- **Probe, don't assume** — every engine/model behavior claim gets a probe or a source line.
  The vendored lance extension loads in the local duckdb CLI (`duckdb -unsigned`, one
  statement per `-c`, extension at `src/*/bin/Release/net10.0/vendor/duckdb/extensions/v1.5.5/osx_arm64/`).
- Embedding micro-probes run as file-based C# apps in the scratchpad
  (`#:project .../Kurrent.Kontext.Embeddings.csproj`, cosine = dot product because outputs
  are L2-normalized). Reference: the session's `pmm12probe2.cs` pattern.
- `MaxTokens` = the model's TRAINED window (`max_seq_length`), never
  `max_position_embeddings`. Both Kontext generations got this wrong before it was fixed.
- Result tables: sorted most→least effective, winner gets 🏆 (standing rule, in memory).
- Benchmarks are deterministic for single-leg chains — one run per config is trustworthy;
  the two-leg RRF chains drift a few thousandths across processes (index-build layout).

## Work Completed

### Tasks Finished

- [x] pMM12 pinned to its trained 128-token window; A/B proved bit-identical on the corpus
- [x] Options seam (`Action<SentencePieceOnnxOptions>?`) through `KontextCorpus`/`KontextStoreFixture`
- [x] `JsonNormalizer` (byte-native, probe-settled rendering) + `IUtf8Normalizer`; 9/9 tests
- [x] Full suite green: 266/266 across the three Kontext unit assemblies
- [x] bge-m3 feasibility probed: FLOAT[1024] column + IVF_HNSW_PQ + vector search all green
  on the vendored engine; INT8 ONNX ≈ 570MB is the practical artifact

### Files Modified

All committed and pushed — tree is clean at `f124461c1`.

### Decisions Made

| Decision | Options Considered | Rationale |
|----------|-------------------|-----------|
| bge-m3 is the upgrade candidate | jina-code (BPE, new path), nomic-embed-code (7B, no ONNX), e5 (needs prefixes) | XLM-R/SentencePiece — drop-in for the existing generator; official+community ONNX; 8192 window fixes the long-records problem |
| INT8 quantization | fp32 2.3GB, fp16 1.1GB | ~570MB, near-lossless for retrieval (0.989 cosine vs fp32, community-validated) |
| Dense vectors only | dense+sparse+ColBERT exports | the store uses dense; sparse/ColBERT would be new capability |
| Generators unsealed | keep sealed + composition | Sérgio's explicit ruling (commit f124461c1) — model-specific subclasses allowed |
| Measurement-first | trust model cards | every rendering/window/config claim this arc made was settled by a probe |

## Pending Work

### Immediate Next Steps

1. **Acquire the model**: download a bge-m3 INT8 dense ONNX + the XLM-R
   `sentencepiece.bpe.model` from Hugging Face (the `hf` CLI + hf-cli skill are available).
   Candidate repos evaluated last session: `gpahal/bge-m3-onnx-int8` (570MB, all outputs),
   `hotchpotch/vespa-onnx-BAAI-bge-m3-only-dense` (dense-only fp16/int8). Verify the export
   is DENSE-output and note which output tensor is the CLS/dense vector.
2. **Load it**: read `OnnxModel.cs` for the file-based path (do NOT embed 570MB as an assembly
   resource); build a `BgeM3` factory mirroring `InterimPmm12` with
   `PoolingMode = Cls, MaxTokens = 8192, InputPrefix = null`. Smoke-probe: scratchpad
   file-based app, embed "probe", check dimension == 1024 and self-cosine == 1.
3. **Rendering probe**: rerun the pmm12probe2 pattern with bge-m3 — raw JSON vs
   `JsonNormalizer.Instance` output. HYPOTHESIS TO TEST: the code-aware model shrinks or
   erases the normalizer's advantage (which would retire the flatten for records).
4. **Corpus benchmark**: add a generator-factory seam to `KontextStoreFixture`/`KontextCorpus`
   (the options seam exists; the MODEL is hardcoded), then a `--model-ab` benchmark mode
   (copy `--max-tokens-ab`): Focused chain, pMM12 vs bge-m3. Note the Dimension gotcha below.
   Also record per-embed latency (bge-m3 is ~5× pMM12 on CPU; the numbers matter).
5. **Report**: sorted tables, 🏆 on winners, beat-or-keep verdict vs the pMM12 baselines
   below. If bge-m3 wins, the follow-ups are: model downloader design, schema Dimension
   migration (retain-replay re-embeds in place — the designed path), floor recalibration.

### Blockers/Open Questions

- [ ] `KontextSchemaTask.Dimension` is a const 384 baked into DDL and `VectorIndexOptions`;
      the corpus fixture creates schema through it. For the benchmark, either temporarily
      change the const to 1024 locally (NOT committed) or add a dimension seam — decide in
      session. FLOAT[1024] end-to-end was probed green on 2026-08-16.
- [ ] Does the INT8 export's graph expose CLS pooling the way `session.Embed`'s Cls path
      expects? Verify against `InferenceSessionExtensions.cs` before trusting numbers.
- [ ] Tokenizer compatibility: bge-m3's sentencepiece model is XLM-R's — confirm the fairseq
      id remap in `Encode` produces sane ids (the generator was verified bit-exact for
      e5-small; bge-m3 shares the tokenizer but VERIFY, don't assume).

### Deferred Items

- Records-indexing build (migration WITH clause for code-tokenizer FTS, `JsonNormalizer`
  wiring at the writer, record-metadata inclusion ruling) — designed, probe-backed, not built.
- Records-retrieval benchmark corpus — without it, records-side quality claims stay directional.
- Model downloader — prerequisite for shipping any non-embedded model.

## Context for Resuming Agent

### Important Context

**The baselines to beat (pMM12, ranking corpus, Focused chain):**
recall@5 **0.4889**, mrr **0.4006**, ndcg@10 **0.4205** — bit-reproducible.
**Rendering probe baselines (pMM12, mean target cosine):** normalizer output 0.5099,
raw JSON 0.4936. If bge-m3's raw-JSON number approaches its normalized number, the
flatten-for-embedding step retires.

Read `.claude/memory/MEMORY.md` first — the rulings are binding (notably:
kontext-lance-fts-tokenizer-contract, benchmark-tables-ordered-with-trophy,
sergio-csharp-style-law, discuss-before-recording, estimate-agent-execution-not-human-days).
Do not re-litigate settled decisions; probe before asserting engine/model behavior; proposals
live in conversation until Sérgio says log/file/commit.

### Assumptions Made

- bge-m3 INT8 community exports are trustworthy for dense retrieval (0.989 cosine vs fp32
  per raludi's validation) — re-verify on the smoke probe.
- The store's `l2` metric + normalized vectors ≡ cosine holds for bge-m3 exactly as for pMM12
  (both normalize; keep `NormalizeEmbeddings = true`).

### Potential Gotchas

- Test results are read from `.artifacts/test-results/<run-id>/*.md` — never from runner
  stdout. Always pass a fresh `--run-id $(uuidgen | tr 'A-Z' 'a-z' | tr -d '-')`.
- The ranking tests are `[Category("Benchmark")]` in `Kurrent.Kontext.Tests` and run under
  the `unit` category filter; scheduler/maintenance tests are `Integration`.
- Corpus init = 419 sequential ONNX embeds — with bge-m3 that is ~5× slower than pMM12;
  budget several minutes per corpus build and background every run.
- Linear automation closes ANY issue referenced from a pushed commit — reference only the
  issue a commit completes.
- `dotnet run` file-based probes and the test runner both build — do not run them
  concurrently (obj/ contention).

## Environment State

### Tools/Services Used

- `scripts/testing/test-runner.cs` (all test runs), `Kurrent.Kontext.Benchmarks` (ranking),
  scratchpad file-based C# probes, `duckdb` CLI + vendored lance extension, `hf` CLI for
  model downloads, `rtk` prefix on shell commands.

### Active Processes

- None — all background tasks completed; tree clean and pushed.

### Environment Variables

- None required beyond the repo defaults.

## Related Resources

- `.claude/context/docs/research/2026-08-15-2318-lance-index-creation-contract/research.md`
  — the full lance index/tokenizer contract (probe-backed)
- `.claude/memory/kontext-lance-fts-tokenizer-contract.md` — condensed rulings + numbers
- Linear: DEV-1875 (Focused pipeline, Done), DEV-1876 (FTS retrain, Done)
- bge-m3 artifacts: huggingface.co/BAAI/bge-m3 · gpahal/bge-m3-onnx-int8 ·
  hotchpotch/vespa-onnx-BAAI-bge-m3-only-dense
