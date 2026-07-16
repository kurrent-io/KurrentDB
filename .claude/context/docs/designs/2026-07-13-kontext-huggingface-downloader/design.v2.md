---
title: HuggingFace.ModelDownloader — design (v2, consolidated)
status: accepted
authors: [sergio]
date: 2026-07-14
tags: [huggingface, onnx, download, tooling, nuget, reusable, modelsharp]
related: [2026-07-12-kontext-models-audit]
---

> Consolidated rewrite of `design.md`. **No decisions changed** — build-ready content up front; landscape
> tables, alternatives, decision log, and history in the appendix. Gaps found in a cold-start review are
> resolved inline (see "Contracts & defaults").

## What it is

`HuggingFace.ModelDownloader` — a small, **standalone, reusable** .NET package that downloads Hugging
Face model bundles (an ONNX model + its tokenizer/config sidecars) **by convention**, as a thin layer
over the managed **`ModelSharp.Hub`** library. We reuse ModelSharp's engine (download, resume, cache
check, SHA-256 verify, HF_TOKEN, revision) and own only the *convention* (manifest + include filter +
cross-repo "family" tokenizer fallback). Pure managed .NET — **no `hf` CLI, no Python, no external
process** — cross-platform, AOT-friendly. Zero coupling to any product (`KurrentDB.Kontext` is only an
example consumer).

## Home

A **standalone package in its own repo/solution** (e.g. Kurrent-SDK or Kurrent Surge), **not** inside
`kurrentdb`. Therefore that repo's central-package-management, `IsPackable=false`, branding, and audit
gates do **not** apply. Publish under your own prefix, e.g. `YourOrg.HuggingFace.ModelDownloader`.
> If it were ever built under `kurrentdb/src`, three adjustments are required: add
> `<PackageVersion Include="ModelSharp.Hub" Version="1.0.3"/>` to `src/Directory.Packages.props` (CPM is
> mandatory there), set `<IsPackable>true</IsPackable>`, and add it to `KurrentDB.slnx`.

## Scope — what we build (everything else is ModelSharp)

1. **Manifest** — `ai-models.json` records + a `System.Text.Json` **source-gen** loader.
2. **Include-pattern filter** — expand `{repo, model}` → the concrete file set by filtering `ListFilesAsync`.
3. **Family fallback** — `config.json → model_type` → `families` → fetch the shared tokenizer from another repo.
4. **Orchestrator** (`IHuggingFaceDownloader.DownloadAsync`) — loop the manifest, wire ModelSharp's primitives.
5. **(Optional) console entry** — a separate small exe project referencing the library (hand-parsed args).
6. **Unit tests** — mocking ModelSharp's `IHubClient`/`IModelDownloader`/`IModelCache`.

**ModelSharp provides (we don't build):** HTTP download, resume, retries/backoff, cache-skip check,
SHA-256 verify, HF_TOKEN resolution, revision handling, repo file listing. Items 2 & 3 are the only real
logic; the rest is glue.

## Manifest (`ai-models.json`)

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

- `models[]` = `{ id, repo, model }` + optional `revision` (SHA pin) and optional `family` (explicit
  override of `model_type` detection). No pooling/prefix — consumer's runtime concern.
- `families` maps a `config.json` `model_type` → a canonical shared-file source. Consumer-defined;
  `xlm-roberta` is illustrative. Absent `families` = treated as empty.
- **JSON casing:** camelCase in the file maps to PascalCase records via
  `[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]`.

## Public API

```csharp
namespace HuggingFace.ModelDownloader;

public sealed record HuggingFaceManifest(IReadOnlyDictionary<string, FamilyFile> Families, IReadOnlyList<ModelEntry> Models);
public sealed record ModelEntry(string Id, string Repo, string Model, string? Revision = null, string? Family = null);
public sealed record FamilyFile(string TokenizerRepo, string TokenizerFile);   // names match the JSON

public sealed record DownloadOptions(
    string CacheRoot, bool Verify = true, bool Strict = false, bool Force = false,
    bool DryRun = false, string? Revision = null, string? Token = null);        // Revision = fallback for models with no pin

public sealed record DownloadResult(
    string Id, string Directory, string ModelPath,
    IReadOnlyList<string> Files, IReadOnlyList<string> Warnings);               // Warnings = e.g. unresolvable family (non-strict)

public interface IHuggingFaceDownloader {
    Task<IReadOnlyList<DownloadResult>> DownloadAsync(HuggingFaceManifest manifest, DownloadOptions options, CancellationToken ct);
}
```

**Composition (no DI):** `new HuggingFaceClient()` (`IHubClient`), `new HttpDownloader()`
(`IModelDownloader`), `new ModelCache()` (`IModelCache`) — all `public sealed`, parameterless-ctor-able;
`IntegrityVerifier` and `HubCredentials` are `public static`. These three interfaces are the test seams.

## Convention algorithm (per model entry)

1. **Resolve:** `revision = entry.Revision ?? options.Revision ?? "main"`; `token = HubCredentials.Resolve(options.Token)`
   (falls back to `HF_TOKEN`/`HUGGING_FACE_HUB_TOKEN`/`HUGGINGFACE_TOKEN` env, then the HF token file).
2. **List:** `IHubClient.ListFilesAsync(repo, revision, token)` → all `RepoFile`s (`Path`, `Size?`, `Sha256?`).
3. **Filter (our convention):** keep `<model>`, `<model>_data` (if present), and every **root**
   `*.json`/`*.model`/`*.txt`. If `<model>` itself is not in the listing → **throw** (hard misconfig,
   both modes).
   - **`DryRun`:** return a plan `DownloadResult` (Files = planned relative paths; no writes). Family
     fallback is **not** evaluated in dry-run (it needs `config.json`'s contents); note this in the plan.
4. **Fetch each kept file:** destination = `Path.Combine(CacheRoot, id, <repo-relative-path>)`. Skip when
   `!Force && IModelCache.IsCached(dest, repoFile)` (existence + size). Else
   `IModelDownloader.DownloadAsync(IHubClient.ResolveUrl(repo, revision, path), dest, token)` (resume
   built in). When `Verify`, `IntegrityVerifier.EnsureAsync(dest, repoFile)` (**throws** on mismatch).
5. **Family fallback:** read `<dir>/config.json → "model_type"` (or `entry.Family` if set). If it matches
   a `families` entry and that `TokenizerFile` is not already in `<dir>`, fetch it from `TokenizerRepo`
   into `<dir>` (steps 2/4). If `model_type` is unknown, or the file is still missing → **add a warning**
   (default) or **throw** (`--strict`).
6. **Return** `DownloadResult { Id, Directory = <CacheRoot>/<id>, ModelPath = <dir>/<model>, Files = all
   files now present under <dir>, Warnings }`.

**Layout:** every file lands under `<CacheRoot>/<id>/<repo-relative-path>` (e.g.
`…/multilingual-e5-small/onnx/model_quantized.onnx`, `…/config.json`, `…/sentencepiece.bpe.model`) — a
**self-contained per-model directory**, incl. the family tokenizer copied in. We manage this layout via
explicit `destinationPath`s, not ModelSharp's HF cache layout.

**Verify caveats:** (a) HF exposes SHA-256 only for **LFS** files, so weights + `sentencepiece.bpe.model`
get true hash checks while small non-LFS descriptors fall back to a size-only check; (b) a **cache hit**
is validated by size only (no re-hash) for speed — a size-correct corrupted cached file passes; use
`--force` to re-fetch. Both accepted.

## Project & dependency

- **TFM:** `net10.0`; `<IsAotCompatible>true</IsAotCompatible>`.
- **Dependency:** pinned `PackageReference Include="ModelSharp.Hub" Version="1.0.3"` (Apache-2.0; one
  transitive dep on the zero-dep core `ModelSharp`). **`System.Text.Json` is in-box on net10 — do NOT add
  a package reference for it.** No `hf`/Python/CliWrap/System.CommandLine.
- **Warnings channel:** surfaced on `DownloadResult.Warnings` (dependency-free); the console entry prints
  them. No `ILogger` dependency forced on library consumers.
- **Shape:** a library (`IHuggingFaceDownloader` + records) and a **separate** optional console project.

## CLI surface (optional console entry, hand-parsed)

```
huggingface-modeldownloader --manifest <path> --out <dir> [--revision <sha>] [--token <t>] [--no-verify] [--strict] [--force] [--dry-run]
```

## Error handling

| Condition | Default | With `--strict` |
|---|---|---|
| Named `model` file absent from the repo listing | **throw** | throw |
| `model_type` unknown / expected family file missing | warn (on `Warnings`), continue | throw |
| Network / transient error | ModelSharp retries (5× backoff), then throws | same |
| Partial/interrupted download | healed on re-run (resume) | same |
| Checksum mismatch (verify on, post-download) | throw (`EnsureAsync`) | throw |

## Testing

Unit tests for our convention layer, mocking `IHubClient`/`IModelDownloader`/`IModelCache` (no network):
include-filter construction (exact resolved file set), family-fallback decision (self-contained /
needs-family / unresolvable) + `--strict`, revision precedence, verify/`--no-verify`/`--dry-run` branches,
warnings population, manifest parsing (camelCase round-trip). Test framework is the publisher's choice.

---

# Appendix — provenance (why the design is what it is)

## A. Landscape of existing .NET options

No first-party Microsoft option exists (MS docs delegate to `huggingface-cli`; Foundry Local needs
Python/Olive; SmartComponents hardcodes two URLs; Windows ML Model Catalog is Windows-only + not
HF-aware). No mainstream ML lib (LLamaSharp, TorchSharp, ML.NET, Sentis) bundles a reusable downloader.
Community packages (✅ · ~ partial · ❌):

| Capability | **ModelSharp.Hub** | ElBruno.HF.Downloader | HuggingfaceHub | tryAGI/HuggingFace | `hf` CLI | roll-your-own |
|---|:--:|:--:|:--:|:--:|:--:|:--:|
| Managed, no Python | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Downloads files | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ (build) |
| File listing primitive | ✅ | ❌ | ✅ | ✅ (meta) | ✅ | build |
| Cross-repo per call | ✅ | ✅ | ✅ | n/a | ✅ | ✅ |
| Resume | ✅ | ✅ | ~ | ❌ | ✅ | build |
| Cache / skip | ✅ | ✅ | ✅ | ❌ | ✅ | build |
| SHA-256 verify | ✅ | ✅ | ~ | ❌ | ✅ | build |
| HF_TOKEN | ✅ | ✅ | ✅ | ✅ | ✅ | build |
| Revision pin | ✅ | ✅ | ✅ | ✅ | ✅ | build |
| Cross-platform incl. Windows | ✅ | ✅ | ~ | ✅ | ❌ | ✅ |
| AOT / trim | ✅ | ~ | ❌ | ✅ | n/a | ✅ |
| net10 | ✅ | ✅ | ❌ | ✅ | n/a | ✅ |
| Maintained | ~ new | ✅ | ❌ stale | ✅ | ✅ | n/a |
| License | Apache-2.0 | MIT | Apache-2.0 | MIT | Apache-2.0 | — |

- **ModelSharp.Hub** → chosen engine (primitives fit our convention; zero-dep; net10; AOT).
- **ElBruno.HF.Downloader** → fallback engine (mature/MIT, but no file listing → enumerate via HF API).
- **HuggingfaceHub** → reference only (stale, Newtonsoft, no net10). **tryAGI** → metadata only.
- **`hf` CLI** → rejected (Python/PATH, no Windows installer). **roll-your-own** → zero-third-party-dep fallback.

## B. ModelSharp.Hub — the fit (verified at commit `372459a6`)

```
IHubClient      : ListFilesAsync(repo, revision, token) → IReadOnlyList<RepoFile>   // RepoFile: Path, Size?, Sha256? (Sha256 only for LFS via lfs.oid)
                  ResolveUrl(repo, revision, repoRelativePath) → string
IModelDownloader: DownloadAsync(url, destinationPath, token, …)   // resume = Range + .part + 5× backoff + atomic move; creates parent dirs
IModelCache     : PathFor(...); IsCached(localPath, expected?)    // IsCached = existence + size only
HubOptions      : Token, Revision="main", VerifyIntegrity=true, ForceDownload, CacheDirectory, MaxConcurrentDownloads=4, Endpoint
IntegrityVerifier: static VerifyAsync→bool, EnsureAsync→throws    // we use EnsureAsync
HubCredentials  : static Resolve(token/env/file)
```

Free at the primitive level: **resume**, revision + token *as params*, parent-dir creation. We wire (all
public helpers): **SHA-256 verify** (`IntegrityVerifier.EnsureAsync`), **cache/skip** (`IModelCache.IsCached`),
**HF_TOKEN resolution** (`HubCredentials.Resolve`). Concrete impls (`HttpDownloader`, `ModelCache`,
`HuggingFaceClient`) are `public sealed` + `new`-able (no DI). We bypass the opinionated
`ModelHub.Get`/`HubPipeline.Load` bundle path and manage our own per-model layout.

## C. Decisions

Download-only manifest · output = `<CacheRoot>/<id>/…` self-contained per model · optional per-model
`revision` pin (options `Revision` is the fallback) · verify on by default (`--no-verify`; `EnsureAsync`)
· unresolvable model → warn / `--strict` · missing named model → always throw · NuGet **library** (not a
`dotnet tool`) · warnings on `DownloadResult` (no logging dep) · tests mock ModelSharp interfaces ·
`families` is consumer config · **live pinned dependency `ModelSharp.Hub` v1.0.3** (vendor = escape
hatch) · LFS-only-SHA-256 + cache-hit-size-only accepted.

## D. Alternatives considered

Built-in MSBuild `DownloadFile` (no repo semantics/verify) · custom MSBuild `Task`/`ToolTask`
(netstandard2.0 + ABI coupling) · packaged `dotnet tool` (pack/feed/install ceremony) ·
`System.CommandLine` (CPM/trim friction) · `hf` CLI (Python/no-Windows) · vendoring ModelSharp (escape
hatch, not default). All rejected for the reasons above.

## E. Supply-chain note

`ModelSharp.Hub` is genuinely live on NuGet (v1.0.3, published 2026-07-04, Apache-2.0, net10) but
**low-adoption**: ~400 total downloads, ~2★, single author, no GitHub releases/CI (published via local
`dotnet nuget push`). Mitigation: **pin the exact version**; vendoring the ~16-file Hub source (Apache-2.0,
zero third-party deps) is the escape hatch if it goes stale — cheap because our layer only touches the
public `IHubClient`/`IModelDownloader`/`IModelCache`/`IntegrityVerifier`/`HubCredentials` surface.

## F. History (condensed)

Originated by consolidating duplicated model-download logic in `KurrentDB.Kontext`. Iterations: scoped
download-by-convention → de-branded to a standalone reusable package → pivoted the engine from the `hf`
CLI (Python, no Windows installer) to managed `ModelSharp.Hub` after a source probe → resolved step-0
caveats (published on NuGet, public impls, resume free / verify+cache+token wired) → chose the live pinned
dependency → cold-start review (fable) resolved into the "Contracts & defaults" now folded above. No open
questions remain.
