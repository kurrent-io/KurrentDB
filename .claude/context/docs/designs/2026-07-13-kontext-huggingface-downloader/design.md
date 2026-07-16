---
title: HuggingFace.ModelDownloader — convention-driven HF model downloader (reusable package, on ModelSharp.Hub)
status: accepted
authors: [sergio]
date: 2026-07-13
tags: [huggingface, onnx, download, tooling, nuget, reusable, modelsharp]
related: [2026-07-12-kontext-models-audit]
---

## Context

Any .NET project that consumes Hugging Face model bundles (an ONNX model + its tokenizer/config
sidecars) needs those files on disk before it can run. Doing it by hand means maintaining per-file
URLs or embedding large binaries; shelling out to the `hf` CLI drags a Python runtime and has no
Windows installer.

This spec defines a **standalone, reusable component** — working name `HuggingFace.ModelDownloader` —
that downloads HF model bundles **by convention**, as a **thin layer over the managed `ModelSharp.Hub`
library**. We reuse ModelSharp for all the mechanics (download, cache, SHA-256 verify, resume, token,
revision, concurrency) and own only the *convention* (a manifest + include patterns + a cross-repo
"family" tokenizer fallback). No `hf`, no Python, no external process — pure managed .NET.

**Standalone by design.** Zero references to any product/org. `KurrentDB.Kontext` appears only as an
*example consumer*. Intended to be published as a small NuGet package (e.g. inside a shared SDK) and
reused across projects.

> Direction confirmed after a landscape review of existing .NET options (tables below) and a source
> probe of ModelSharp.Hub's public API.

## Goals

- **Don't reinvent the wheel.** Reuse an existing managed engine for the download mechanics; own only
  the convention on top.
- **Convention over configuration.** A manifest entry is `{ id, repo, model }` (+ optional `revision`);
  everything else (companion weights, config/tokenizer sidecars, shared family tokenizer) is derived.
- **Fully self-contained & cross-platform.** Pure managed .NET — no Python, no `hf` CLI, no external
  process; works on Windows/Linux/macOS; AOT/trim-friendly.
- **Reproducible & verifiable.** Optional per-model revision pin; SHA-256 verification on by default.
- **Reusable.** Publishable as a NuGet library with no product-specific dependencies.

## Non-goals

- No tokenization, pooling, or inference — only puts files on disk.
- No knowledge of any consumer's runtime config (pooling/prefix).
- No dependency on any specific product/org.
- Not a `dotnet tool`; a NuGet **library** (optional console entry). No `hf`/Python.
- Does not prove a tokenizer is *correct* — only that the expected file for a configured family is
  present; correctness is the consumer's concern.

## Landscape — existing .NET options (why we build on ModelSharp.Hub)

There is **no first-party Microsoft option**: MS docs tell you to run `huggingface-cli download …`
(ONNX Runtime GenAI), or use Python/Olive (Foundry Local); SmartComponents hardcodes two URLs; Windows
ML Model Catalog is Windows-only and not HF-aware. No mainstream ML lib (LLamaSharp, TorchSharp, ML.NET,
Sentis) bundles a reusable downloader — all say "fetch the file yourself." That leaves community
packages:

**Feature matrix** (✅ yes · ~ partial/unverified · ❌ no):

| Capability | **ModelSharp.Hub** | ElBruno.HF.Downloader | HuggingfaceHub | tryAGI/HuggingFace | `hf` CLI | roll-your-own HttpClient |
|---|:--:|:--:|:--:|:--:|:--:|:--:|
| Managed .NET, no Python | ✅ | ✅ | ✅ | ✅ | ❌ (Python) | ✅ |
| Downloads files to disk | ✅ | ✅ | ✅ | ❌ (metadata/inference only) | ✅ | ✅ (you build) |
| File **listing** primitive | ✅ `ListFilesAsync` | ❌ (name files) | ✅ `allowPatterns` | ✅ (metadata) | ✅ `--include` | build |
| Cross-repo (per-call repo) | ✅ | ✅ | ✅ | n/a | ✅ | ✅ |
| Resume | ✅ (impl) | ✅ | ~ | ❌ | ✅ | build |
| Cache / skip-if-exists | ✅ | ✅ | ✅ | ❌ | ✅ | build |
| SHA-256 verify | ✅ (default on) | ✅ | ~ (ETag) | ❌ | ✅ (`cache verify`) | build |
| HF_TOKEN auth | ✅ | ✅ | ✅ | ✅ | ✅ | build |
| Revision pin | ✅ | ✅ | ✅ | ✅ | ✅ | build |
| Cross-platform incl. Windows | ✅ | ✅ | ~ (symlinks) | ✅ | ❌ (installer: Linux/macOS) | ✅ |
| AOT / trim-friendly | ✅ (zero deps) | ~ (untested) | ❌ (Newtonsoft) | ✅ | n/a | ✅ |
| net10 target | ✅ | ✅ (net8+net10) | ❌ (net6–8) | ✅ | n/a | ✅ |
| Dependencies | **zero** (HttpClient) | tiny (DI/logging) | Newtonsoft | small | Python+huggingface_hub | none |
| Maintained | ~ (new, active) | ✅ (this month) | ❌ (stale 2024) | ✅ | ✅ (HF) | n/a |
| Adoption | ~2★, unreleased? | ~8★, ~12K dl | ~20★, ~19K dl | ~56★, ~360K dl | huge | n/a |
| License | Apache-2.0 | MIT | Apache-2.0 | MIT | Apache-2.0 | — |

**Verdict per option:**

| Option | Role for us |
|---|---|
| **ModelSharp.Hub** | **Chosen engine.** Exposes exactly the primitives our convention needs, zero-dep, AOT, net10. Caveats: very new; confirm publish + construction path; vendor if needed (Apache-2.0). |
| ElBruno.HF.Downloader | Strong fallback engine — more mature/published, MIT — but no `ListFiles`, so we'd enumerate files via the HF API ourselves. |
| HuggingfaceHub | Reference only (stale, Newtonsoft, no net10). Good source to borrow include/snapshot logic from (Apache-2.0). |
| tryAGI/HuggingFace | Only for Hub *metadata* if ever needed; does not download files. |
| `hf` CLI | Rejected — Python/PATH dependency, no Windows installer; the thing we're eliminating. |
| roll-your-own HttpClient | Fallback if we want zero third-party deps at all; more code to own. |
| Microsoft first-party | Nothing suitable exists. |

## ModelSharp.Hub — the fit (from its `Abstractions.cs`)

```
IHubClient      : ListFilesAsync(repo, revision, token) → IReadOnlyList<RepoFile>   // RepoFile: Path, Size?, Sha256?
                  ResolveUrl(repo, revision, repoRelativePath) → string
IModelDownloader: DownloadAsync(url, destinationPath, token, progress, …)
IModelCache     : PathFor(repo, revision, path);  IsCached(localPath, expected?)
HubOptions      : Token, Revision="main", VerifyIntegrity=true, ForceDownload,
                  CacheDirectory, MaxConcurrentDownloads=4, Endpoint="https://huggingface.co"
```

| Our need | ModelSharp primitive |
|---|---|
| Download a specific named onnx | `ResolveUrl` + `DownloadAsync` |
| Enumerate repo, filter by our include patterns | `ListFilesAsync` → filter `RepoFile.Path` (our convention) |
| **Family fallback from a different repo** | same primitives with the other `repo` arg |
| Revision / SHA pin | `HubOptions.Revision` |
| HF_TOKEN auth | `HubOptions.Token` |
| SHA-256 verify | `VerifyIntegrity` + `RepoFile.Sha256` |
| Cache / skip | `IModelCache.IsCached` / `PathFor`, `ForceDownload` |
| Resume | `HttpDownloader` impl (Range + `.part`) |

We **bypass** its opinionated `ModelHub.Get`/`HubPipeline.Load` bundle path (that's "it decides") and
compose the primitives (that's "we decide"), layering the convention below.

## Decisions (resolved)

1. **Engine:** build on **ModelSharp.Hub** primitives; reuse its download/cache/verify/resume/token/
   revision. No `hf`, no Python, no CliWrap, no `System.CommandLine`.
2. **Manifest scope:** download-only (`{ id, repo, model, revision? }` + `families`).
3. **Output location:** consumer-supplied `CacheRoot` (maps to `HubOptions.CacheDirectory`).
4. **Pinning:** optional per-model `revision` → `HubOptions.Revision`; absent → `main`.
5. **Verify:** on by default (our layer calls `IntegrityVerifier.VerifyAsync`); `--no-verify` disables.
   SHA-256 is enforced for LFS files (weights, `sentencepiece.bpe.model`); small non-LFS descriptors get
   a size-only check (HF exposes no sha for them).
6. **Unresolvable model:** detected via `config.json → model_type` vs the consumer's `families` config;
   default warn-and-continue, `--strict` → throw.
7. **Distribution:** NuGet **library** package (optional console entry). Not a `dotnet tool`.
8. **Tests:** unit tests for our convention layer, mocking ModelSharp's `IHubClient`/`IModelDownloader`/
   `IModelCache` interfaces (no network).
9. **`families` is consumer config, not built-in** (the `xlm-roberta` entry below is illustrative).
10. **Dependency posture — DECIDED: live NuGet dependency.** Take a pinned
    `PackageReference Include="ModelSharp.Hub" Version="1.0.3"` (Apache-2.0, net10, one transitive dep on
    the zero-dep core `ModelSharp`). Vendoring the ~16-file source remains the escape hatch if the package
    ever goes stale/unavailable — cheap to switch to, since our layer only touches the public
    `IHubClient`/`IModelDownloader`/`IModelCache`/`IntegrityVerifier`/`HubCredentials` surface.

## Design

### Project / package

- **Working name:** `HuggingFace.ModelDownloader` (publish under your own prefix, e.g.
  `YourOrg.HuggingFace.ModelDownloader`, to avoid squatting the official `HuggingFace.*` namespace).
- **TFM:** `net10.0`. **Deps:** `ModelSharp.Hub` (pinned v1.0.3) + `System.Text.Json` (source-gen).
  No Python, no external process, no CliWrap, no `System.CommandLine`.
- **Shape:** a library exposing `IHuggingFaceDownloader` + manifest/result records, plus an optional
  thin console entry (hand-written arg parser).

### Manifest (`ai-models.json`) — download-scoped

```json
{
  "families": {
    "xlm-roberta": { "tokenizerRepo": "intfloat/multilingual-e5-small", "tokenizerFile": "sentencepiece.bpe.model" }
  },
  "models": [
    { "id": "multilingual-e5-small", "repo": "Xenova/multilingual-e5-small", "model": "onnx/model_quantized.onnx", "revision": "<commit-sha>" },
    { "id": "bge-m3",                "repo": "Xenova/bge-m3",                "model": "onnx/model_quantized.onnx" }
  ]
}
```
*(Illustrative.)* `families` is a generic, consumer-defined mechanism ("if a repo lacks a file its
family needs, fetch it from the family's source").

### Public API (sketch)

```csharp
namespace HuggingFace.ModelDownloader;

public sealed record HuggingFaceManifest(IReadOnlyDictionary<string, FamilyFile> Families, IReadOnlyList<ModelEntry> Models);
public sealed record ModelEntry(string Id, string Repo, string Model, string? Revision = null, string? Family = null);
public sealed record FamilyFile(string SourceRepo, string FileName);

public sealed record DownloadOptions(string CacheRoot, bool Verify = true, bool Strict = false, bool Force = false, bool DryRun = false, string? GlobalRevision = null);
public sealed record DownloadResult(string Id, string Directory, string ModelPath, IReadOnlyList<string> Files);

public interface IHuggingFaceDownloader {
    Task<IReadOnlyList<DownloadResult>> DownloadAsync(HuggingFaceManifest manifest, DownloadOptions options, CancellationToken ct);
}
```

Implementation composes ModelSharp's `IHubClient` + `IModelDownloader` + `IModelCache` (injected) —
which are also the **test seams**.

### Download convention (per model entry, over ModelSharp primitives)

1. **List:** `IHubClient.ListFilesAsync(repo, revision)` → all `RepoFile`s.
2. **Filter (our convention):** keep `<model>`, `<model>_data` (if present), and every root
   `*.json` / `*.model` / `*.txt`. This is the include-pattern logic ModelSharp doesn't have.
   - **If `DryRun`:** emit this file plan and stop.
3. **Fetch each kept file (composing ModelSharp primitives):** resolve the token once via
   `HubCredentials.Resolve(...)`; then per file — skip when `IModelCache.IsCached(PathFor(...), repoFile)`;
   else `DownloadAsync(ResolveUrl(repo, revision, path), PathFor(...), token)` (resume is built in); then,
   when `Verify` is on, `IntegrityVerifier.VerifyAsync(path, repoFile)`.
4. **Family fallback:** read `config.json → "model_type"`; if it matches a `families` entry and that
   `FileName` is absent, `ListFilesAsync`/`DownloadAsync` it from `SourceRepo`. Unknown type / still
   missing → **unresolvable**: warn (default) or throw (`--strict`).
5. **Return** `DownloadResult { Id, Directory, ModelPath, Files }`.

### CLI surface (optional console entry, hand-parsed)

```
huggingface-modeldownloader --manifest <path> --out <dir> [--revision <sha>] [--no-verify] [--strict] [--force] [--dry-run]
```

### Error handling / failure modes

| Condition | Default | With `--strict` |
|---|---|---|
| `model_type` unknown / expected family file missing | warn, continue | throw |
| Network / transient error | ModelSharp retries; then throw | same |
| Partial/interrupted download | healed on re-run (cache + resume) | same |
| Checksum mismatch (verify on) | throw | throw |

*(No "hf missing" row — there is no external CLI anymore.)*

### Packaging & consumption

- **Library (primary):** consumers add a `PackageReference` and call `DownloadAsync`.
- **Console (optional):** the same code path for manual/CI/MSBuild use.
- **Dependency:** pinned `PackageReference` to `ModelSharp.Hub` v1.0.3 (Apache-2.0). Vendoring the
  source is the escape hatch if it ever goes stale.

### AOT / trim

- `[JsonSerializable]` source-gen for the manifest; no reflection.
- ModelSharp.Hub is zero-dependency (HttpClient only) and net10 — keeps the AOT story clean. Mark
  `<IsAotCompatible>true</IsAotCompatible>`.

### Testing

Unit tests for our convention layer, mocking ModelSharp's `IHubClient` / `IModelDownloader` /
`IModelCache` (no network, no real HF): include-filter construction from a `Model` path; family-
fallback decision (self-contained vs needs-family vs unresolvable) + `--strict`; verify/`--no-verify`/
`--dry-run` branches; manifest parsing. Framework is the publisher's choice.

## Step-0 caveats — RESOLVED (source + NuGet probe, 2026-07-14)

1. **Construction path — ✅ usable directly.** Concrete impls are all `public sealed` with public ctors:
   `HttpDownloader(HttpClient? = null)`, `ModelCache(HubOptions? = null)`, `HuggingFaceClient(HttpClient? = null, endpoint)`.
   No DI extension exists — you just `new` them (as ModelSharp's own tests do).
2. **Availability — ✅ live on NuGet.** `ModelSharp.Hub` v1.0.3 (published 2026-07-04), Apache-2.0,
   `net10.0`, single dependency on the zero-dep core `ModelSharp`. GitHub "no releases/tags" is also
   true — the author publishes via local `dotnet nuget push`, no CI. **Adoption is tiny (~400 downloads,
   ~2★, single author)** → mitigated by pinning v1.0.3 (Decision 10); vendoring is the escape hatch.
3. **Verify / resume / cache wiring — resolved (we compose primitives):**
   - **Resume — FREE:** `HttpDownloader.DownloadAsync` does `Range` + `.part` temp + 5-attempt
     exponential backoff + atomic `File.Move`, self-contained.
   - **SHA-256 verify — WE WIRE IT:** verification is the public static `IntegrityVerifier`, invoked only
     by the high-level orchestrator; our layer calls `IntegrityVerifier.VerifyAsync(path, repoFile)` after
     download.
   - **Cache / skip — WE WIRE IT:** `DownloadAsync` always re-downloads; our layer calls
     `IModelCache.IsCached(path, repoFile)` first.
   - **Revision + token — FREE as params;** `HF_TOKEN` *resolution* is the public static
     `HubCredentials.Resolve(...)` (env `HF_TOKEN`/`HUGGING_FACE_HUB_TOKEN`/`HUGGINGFACE_TOKEN` → token file),
     which our layer calls once.
   Net: our layer = list → filter → (per file) `IsCached?` → `DownloadAsync` → `VerifyAsync`, + resolve
   token once. Every piece is public; resume is free; verify/cache/token are ~a dozen lines total.
4. **Integrity limitation (real, minor):** HF exposes SHA-256 only for **LFS** files, so the model
   weights + `sentencepiece.bpe.model` (LFS) get true hash checks, while small **non-LFS** descriptors
   (`config.json`, etc.) fall back to a **size-only** check. Acceptable — the load-bearing files are LFS.

## Alternatives Considered

See the landscape tables above. In short: **ElBruno.HF.Downloader** is the fallback engine (more
mature, but no file-listing → enumerate via HF API); **HuggingfaceHub** is reference-only (stale/
Newtonsoft); **`hf` CLI** rejected (Python/PATH/no-Windows); **roll-your-own** kept as the
zero-third-party-dependency fallback; **Microsoft** ships nothing suitable.

## Revision

- **2026-07-14 (d):** Final calls — take the **live NuGet dependency** (pinned `ModelSharp.Hub` v1.0.3),
  not vendoring (vendoring kept as the escape hatch). LFS-only-SHA-256 limitation accepted as-is. No
  open questions remain.
- **2026-07-14 (c):** Resolved all step-0 caveats via a source/NuGet probe. `ModelSharp.Hub` is live on
  NuGet (v1.0.3, Apache-2.0, net10, ~400 downloads); concrete impls are public/`new`-able (no DI);
  resume is free at the primitive level while SHA-256 verify, cache-skip, and `HF_TOKEN` resolution are
  wired by our layer (all public helpers — `IntegrityVerifier`, `IModelCache`, `HubCredentials`).
  Recommended **vendoring** the Hub (~16 files) over a NuGet dependency given its low adoption / no CI.
  Recorded the LFS-only-SHA-256 limitation (load-bearing files are LFS → verified).
- **2026-07-14 (b):** Pivoted the engine to **ModelSharp.Hub**. A source probe of its `Abstractions.cs`
  showed clean primitives (`ListFilesAsync`, `ResolveUrl`, `DownloadAsync`, `IModelCache`, `HubOptions`)
  that fit our convention exactly and support cross-repo fetches — correcting the earlier "bundle-only /
  can't control files / can't cross-repo" read. The component drops from "implement a downloader" to "a
  thin convention layer over ModelSharp," eliminating `hf`/Python/CliWrap entirely. Added the landscape
  feature matrix and per-option verdicts; added step-0 caveats (confirm publish/construction/verify
  wiring; vendor fallback, Apache-2.0).
- **2026-07-14 (a):** De-branded from `KurrentDB.Kontext.*` to a reusable, standalone package.
