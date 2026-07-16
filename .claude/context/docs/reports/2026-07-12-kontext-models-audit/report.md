---
title: KurrentDB.Kontext.Models — Project Audit
type: audit
date: 2026-07-12
author: sergio
tags: [kontext, onnx, embeddings, packaging, build]
scope: src/KurrentDB.Kontext.Models and its consumers
---

## Summary

`KurrentDB.Kontext.Models` is a resources-only `netstandard2.0` assembly whose entire job is to
ship four ONNX models (embedding = all-MiniLM-L6-v2, cross-encoder = ms-marco-TinyBERT-L2-v2, each in
AVX2 + AVX-512 quantized variants) plus two `vocab.txt` files as **embedded assembly resources**. It
contains **no C# code**. The models are pulled from HuggingFace at build time by an MSBuild target and
are **not** committed to git.

The core idea (download-on-build, embed as resources, pick the CPU-appropriate variant at runtime) is
defensible, but the execution has one **reproducible build-breaking bug**, a real **supply-chain gap**
(floating `main` ref, no checksum), a **dangling `THIRD-PARTY-NOTICES` reference** with no model
attribution anywhere, and ~26 MB of **dead-weight binaries** baked into a 55.6 MB DLL. It is genuinely
**live** in the shipped server (the `EmbeddingsProvider.Local` default and the cross-encoder reranker),
but the code that consumes it lives in a folder misleadingly named `Prototype/`, next to a newer,
fully-written replacement path that currently has **zero callers**.

Verdict: not throwaway, but currently the messiest corner of the Kontext stack — a mix of a fragile
build, a compliance gap, and two half-migrated implementations sharing one project.

## Findings

### How it is used (the "live vs dead" map)

- **Consumers** (`ProjectReference`, no `PackageReference` — never consumed as a NuGet package):
  `KurrentDB.Kontext`, `KurrentDB.Kontext.Embeddings`, `KurrentDB.Kontext.Embeddings.Playground`,
  `KurrentDB.Plugins.Kontext`. In both `KurrentDB.slnx:15` and legacy `src/KurrentDB.sln:215`.
- **The single loader** is `Kurrent.Kontext.Embeddings.Prototype.ModelManager`
  (`src/KurrentDB.Kontext.Embeddings/Prototype/ModelManager.cs`). It does
  `Assembly.Load("KurrentDB.Kontext.Models")` then `GetManifestResourceStream($"KurrentDB.Kontext.Models.{suffix}")`.
- **Live path (verified):** `ModelManager` is registered in production DI at
  `src/KurrentDB.Kontext/ServiceCollectionExtensions.cs:58`; `CrossEncoderService` (reranker) is
  registered unconditionally and invoked from the search flow (`KontextService.cs:172`);
  `EmbeddingService` is registered when `EmbeddingsProvider.Local` is selected, which is the **default**
  (`Config.cs:48`). The `KontextPlugin` is added to the live plugin list in
  `ClusterVNodeHostedService.cs:286`, gated only by the `KONTEXT` license entitlement.
- **Dead/unwired path (verified):** the newer `KurrentDB.Kontext.Embeddings/Local/*` implementation
  (`DefaultLocalEmbeddingGenerator`, `BertOnnxEmbeddingGenerator`, `HandRolledWordPieceTokenizer`, …)
  and its DI extensions `AddDefaultLocalEmbeddings` / `AddBertOnnxEmbeddings` have **no callers anywhere**
  (`grep` returns none). It is a designed-but-not-adopted replacement; notably it does **not** reference
  this Models project — model sourcing is left to the caller.
- `KurrentDB.Kontext.V3`, `KurrentDB.Kontext.Mcp`, `KurrentDB.Kontext.Reloaded` (the "next rework")
  do **not** touch Models or embeddings at all yet.
- **Variant selection is real and lives in exactly one place:**
  `ModelManager.DetectBestQuantizedModel()` picks `model_qint8_avx512` when `Avx512F.IsSupported`, else
  `model_quint8_avx2`. It is the sole `Avx512`/`avx2` selection logic in the repo.

### Ranked problems (most severe first)

| # | Severity | Problem |
|---|----------|---------|
| 1 | High (correctness) | Build-breaking condition bug in `DownloadModelsIfMissing` |
| 2 | High (supply chain) | Floating `main` ref + no checksum on downloaded models |
| 3 | Medium (compliance) | `THIRD-PARTY-NOTICES` referenced but nonexistent; no model attribution |
| 4 | Medium (efficiency) | ~26 MB dead-weight variants in a 55.6 MB embedded DLL |
| 5 | Medium (reliability) | Network-required build, zero retries, no offline fallback |
| 6 | Medium (maintainability) | Production loader lives in a folder named `Prototype/` |
| 7 | Low–Med (dead code) | Duplicate `Local/*` embedding path with zero callers |
| 8 | Low (hygiene) | `netstandard2.0` TFM unjustified |
| 9 | Low (hygiene) | Redundant `ProjectReference` in `Plugins.Kontext` |
| 10 | Low (caveated) | Dynamic `Assembly.Load` + string resource lookup vs stated AOT/no-reflection conventions |

#### 1 — Build-breaking condition bug (High, correctness) — VERIFIED

The target's outer `Condition` only checks the two `_quint8_avx2` files:

```xml
Condition="!Exists('$(KontextModelsDir)embedding/model_quint8_avx2.onnx') Or
           !Exists('$(KontextModelsDir)cross-encoder/model_quint8_avx2.onnx')"
```

…but the target is responsible for **six** files. If the two AVX2 files exist while any of the other
four (both AVX-512 variants, both `vocab.txt`) are missing — a partial/interrupted download, or a dev
deleting the big variant to save space — the outer condition is `false`, the whole target is skipped,
the per-file `DownloadFile` guards never evaluate, and the C# compiler fails hard:
`CSC error CS1566: Error reading resource '…model_qint8_avx512.onnx' -- Could not find file`. Recovery
requires manually deleting the AVX2 files too. **Confirmed by reading the condition logic; the subagent
also reproduced CS1566 in an isolated copy.** Fix: make the outer condition cover all six files, or drop
it entirely and rely on the existing per-file `Condition="!Exists(...)"` guards.

#### 2 — Floating ref + no checksum (High, supply chain) — VERIFIED

All six `SourceUrl`s use `huggingface.co/.../resolve/main/...` — a mutable branch, not a pinned commit
SHA — and nothing hashes the downloaded bytes. Two builds at different times can silently embed
different models under the same URL, and a corrupted/tampered download is embedded with no error. Fix:
pin to a commit SHA (`resolve/<sha>/...`) and verify SHA-256 per file, failing the build on mismatch.

#### 3 — Dangling `THIRD-PARTY-NOTICES` + missing attribution (Medium, compliance) — VERIFIED

`<None Include="…/../../THIRD-PARTY-NOTICES" Pack="true" … />` points at a file that **does not exist**
anywhere in the repo or git history (`find` / `git ls-files` both empty). `NOTICE.md` (the repo's real
notices file) contains no MiniLM / TinyBERT / HuggingFace attribution either — so the redistributed ML
artifacts (Apache-2.0 upstream) have **no attribution anywhere**. Currently latent only because
`IsPackable=false` (see #8's cousin below); forcing `dotnet pack` fails with `NU5019: File not found`.
Fix: create a real root `THIRD-PARTY-NOTICES` with model attributions, or drop the dead `Pack` item
until packaging is real.

#### 4 — Dead-weight embedded variants (Medium, efficiency) — VERIFIED

All six files are `<EmbeddedResource>`, so both AVX2 **and** AVX-512 variants of both models compile
into the DLL regardless of target CPU. Measured output: `KurrentDB.Kontext.Models.dll = 55,577,088
bytes` — essentially 100% blobs. At runtime `ModelManager` loads only one variant per model, so ~26 MB
(the unused embedding variant ~22 MB + unused cross-encoder variant ~4.3 MB) is dead weight resident in
every process on every machine, plus larger artifacts and slower CI copy. Embedding is the wrong
mechanism at this size; prefer `Content` copy-to-output, a lazy first-use download cache, or per-arch
assets.

#### 5 — Network-required build, no resilience (Medium, reliability) — VERIFIED (docs)

The MSBuild `DownloadFile` task defaults to `Retries=0` and exposes no timeout; the csproj overrides
neither. A clean checkout requires egress to `huggingface.co`, and any transient failure or air-gapped
CI runner fails the build on the first attempt. Fix: set `Retries`, document the network requirement,
and consider an internal mirror.

#### 6 — Production code in a `Prototype/` folder (Medium, maintainability) — VERIFIED

`ModelManager`, `EmbeddingService`, and `WordPieceTokenizer` were moved into
`KurrentDB.Kontext.Embeddings/Prototype/`, yet they are the **only** implementation wired into the
shipped server. The directory name invites someone to treat load-bearing code as throwaway; deleting it
breaks the `EmbeddingsProvider.Local` default and the reranker. Fix: rename the folder to reflect its
real role, or complete the migration to `Local/*` and delete the prototype.

#### 7 — Duplicate dead replacement path (Low–Med, dead code) — VERIFIED

`Local/HandRolledWordPieceTokenizer.cs` ≈ `Prototype/WordPieceTokenizer.cs`, and
`Local/DefaultLocalEmbeddingGenerator.cs` (doc-commented "ported verbatim from KurrentDB.Kontext")
duplicates `Prototype/EmbeddingService.cs`'s mean-pool/L2-normalize logic. The `Local/*` path has zero
callers. Two maintenance surfaces for identical behavior until one is deleted or the wiring is switched.

#### 8 — `netstandard2.0` unjustified (Low, hygiene)

Every consumer is `net10.0` and the project is never packaged externally (`IsPackable=false` repo-wide).
For a resources-only assembly the TFM buys nothing. Fix: inherit `net10.0` unless external distribution
becomes a real, stated goal (in which case make that intent explicit).

#### 9 — Redundant `ProjectReference` (Low, hygiene)

`KurrentDB.Plugins.Kontext.csproj:20` references Models directly, but nothing in that project touches
the resources — the only consumer (`Prototype.ModelManager`) is reached transitively via
`KurrentDB.Kontext`. The direct reference is redundant.

#### 10 — Dynamic load vs stated AOT/no-reflection conventions (Low, caveated) — VERIFIED nuance

`ModelManager` uses `Assembly.Load("KurrentDB.Kontext.Models")` + a computed string passed to
`GetManifestResourceStream`. This is at odds with the repo's **stated** AOT / no-reflection conventions
(CLAUDE.md dotnet-conventions), but those conventions are **not enforced in the build files**: no
`PublishAot` or `IsAotCompatible` appears in any `.csproj`/`.props` in the repo. So this is a latent risk
against stated intent, not an enforced-policy violation, and it works today because the assembly is in
the normal `ProjectReference` build closure. Fix (only if/when AOT is actually adopted): add a
`PublishAot` smoke test for the consuming host rather than assuming this survives trimming.

## Recommendations

Priority order:

1. Fix the download-target condition (#1) — it is a live, reproducible build breaker.
2. Pin the HuggingFace ref to a commit SHA and add SHA-256 verification (#2).
3. Either create a real `THIRD-PARTY-NOTICES` with model attribution or remove the dead `Pack` item (#3).
4. Stop embedding both CPU variants — move to content/lazy-download or per-arch packaging (#4), which
   also shrinks the artifact and eases the AOT concern (#10).
5. Add `Retries` + document the network requirement / add a mirror (#5).
6. Resolve the `Prototype/` vs `Local/*` split (#6, #7): pick one embedding path, delete the other,
   rename to reflect production status.
7. Sweep the low-hygiene items (#8, #9) opportunistically.

## Method

- Scope: `src/KurrentDB.Kontext.Models/` (csproj, `.gitignore`, embedded ONNX/vocab assets) and every
  consumer found by grep across the repo.
- Two parallel Sonnet subagents: one on consumption/runtime loading, one on internals/build/packaging.
  Both were instructed read-only.
- Load-bearing claims independently re-verified by the author: `git ls-files` / `git log --all` (binaries
  not tracked, never in history), `find`/`git ls-files` for `THIRD-PARTY-NOTICES` (absent), built DLL
  size (`55,577,088` bytes), `grep` for `Local/*` DI-extension callers (none), production `ModelManager`
  registration (`ServiceCollectionExtensions.cs:58`), and repo-wide `PublishAot`/`IsAotCompatible` (none).
- Not covered: runtime correctness of the models/tokenizers themselves (embedding accuracy, reranker
  quality) — behavioral, out of scope for this static audit; and whether the `Local/*` path is intended
  to keep or drop the Models dependency (an open design decision, not a defect).
