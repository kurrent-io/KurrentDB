---
title: ONNX model loading — interim embedded-resource bridge (before the HF downloader)
status: accepted
authors: [sergio]
date: 2026-07-14
tags: [kontext, embeddings, onnx, model-loading, aot, bridge]
related: [2026-07-13-kontext-huggingface-downloader, 2026-07-11-kontext-embeddings-sk-migration, 2026-07-12-kontext-models-audit]
---

## Context

The new embeddings system (`KurrentDB.Kontext.Embeddings`) resolves a model's bytes through
`OnnxModel` (a lazy handle: `ReadModel()` + `ReadAsset(name)`) and `OnnxModelRegistry` (`Get(key) →
OnnxModel`, plus `FromConfiguration`). It deliberately says **nothing** about *how* the bytes got onto
disk — that is a separate concern.

The real acquisition mechanism is the **`HuggingFace.ModelDownloader`** (accepted, not yet built — see
[`2026-07-13-kontext-huggingface-downloader`](../2026-07-13-kontext-huggingface-downloader/design.md)),
a standalone/de-branded package that downloads HF bundles to a cache directory. Until it lands (target
~2 weeks) production needs a model present by other means.

Two loaders exist today:
- **Embedded** — the legacy `Prototype/ModelManager` reads the shipped all-MiniLM model + vocab as
  manifest resources out of the `KurrentDB.Kontext.Models` assembly (`Assembly.Load` + AVX-variant pick).
  This is the current production path (implementation A).
- **Playground downloader** — an ad-hoc `HttpClient` fetch into a gitignored `models/` cache; dev/eval
  only (and what the C tests read).

**This spec defines the interim bridge**: how the new system obtains its model **without waiting for the
downloader and without coupling to the legacy prototype code**. Hard constraints from the owner:
1. `OnnxModelRegistry.Get` is **read-only** — it never downloads; assets must be present up front.
2. Production interim uses the **build-time-download-and-embed** mechanism that exists today, in the
   existing `KurrentDB.Kontext.Models` assembly (we can't move that now).
3. **Zero coupling** — the new ONNX components reference nothing in the legacy prototype (`ModelManager`,
   `EmbeddingService`, `Prototype/`) *and* nothing in the downloader package. Connections are made by
   contract (a read stream; a directory layout), never by shared types.

## Design

### The pivot: read vs. acquire

- **Read** — where a model's bytes come from *right now* (an embedded assembly resource, or a file on
  disk). This is `OnnxModel`'s job; it already abstracts it (Func-backed `ReadModel`/`ReadAsset`).
- **Acquire** — how bytes *get there* (build-time embed today; HF download later). Independent of reading.

The bridge adds one **read** source (embedded) and one **acquire** mechanism (build-time embed). The
downloader later swaps only the *acquire* side.

### New `OnnxModel` factory — `FromEmbeddedResources`

Reads model + companion assets from **any** assembly's manifest resources. Generic over `Assembly`, so
it knows nothing about `KurrentDB.Kontext.Models` or the prototype. AOT-safe: `GetManifestResourceStream`,
**not** `Assembly.Load("…")` (the reflection smell it replaces).

```csharp
// OnnxModel.cs — additional factory
public static OnnxModel FromEmbeddedResources(
    string name,
    Assembly assembly,
    string modelResource,
    IReadOnlyDictionary<string, string> assetResources) =>
    new(name,
        () => OpenResource(assembly, modelResource),
        asset => OpenResource(assembly, assetResources[asset]));

static Stream OpenResource(Assembly assembly, string resource) =>
    assembly.GetManifestResourceStream(resource)
        ?? throw new InvalidOperationException(
            $"Embedded resource '{resource}' not found in {assembly.GetName().Name}.");
```

### Getting the `KurrentDB.Kontext.Models` assembly AOT-safely

We reuse `KurrentDB.Kontext.Models` (the existing, build-populated resource assembly — models stay where
they are). To hand `FromEmbeddedResources` its `Assembly` without `Assembly.Load(string)`, add a **one-line
public marker** to that project and use `typeof(...).Assembly`:

```csharp
// in KurrentDB.Kontext.Models — a marker only; no logic, no coupling to ModelManager
public sealed class KontextModelsAssembly;
```

The interim already edits `KurrentDB.Kontext.Models` (to download + embed pMM12), so the marker rides
along for free — a purely additive class, no `Assembly.Load`, no reflection.

### Interim composition-root wiring

Uses the generator's **direct `(OnnxModel, options)` constructor** — no registry, no `ModelManager`,
no `Prototype/`. Interim production model = **`paraphrase-multilingual-MiniLM-L12-v2` (pMM12)**.

```csharp
var model = OnnxModel.FromEmbeddedResources(
    "paraphrase-multilingual-MiniLM-L12-v2",
    typeof(KontextModelsAssembly).Assembly,
    modelResource:  "KurrentDB.Kontext.Models.<…>.model.onnx",
    assetResources: new() { ["sentencepiece.bpe.model"] = "KurrentDB.Kontext.Models.<…>.sentencepiece.bpe.model" });

services.AddEmbeddingGenerator(sp =>
    new SentencePieceOnnxEmbeddingGenerator(model, new SentencePieceOnnxOptions { InputPrefix = null })); // pMM12 uses no prefix
```

`OnnxModelRegistry` / `AddOnnxModelRegistry` / the `(OnnxModelRegistry, options)` ctor are **not used in
the interim** — they are the downloader-era path (registry over a populated cache dir).

### Build-time download-and-embed

Extend the existing MSBuild download target in `KurrentDB.Kontext.Models` (the one that already fetches
all-MiniLM) to also fetch pMM12's ONNX + `sentencepiece.bpe.model` and embed them. Fetched at build,
**not committed to git** — same as today.

```xml
<Target Name="DownloadEmbeddingModels" BeforeTargets="ResolveReferences">
  <DownloadFile SourceUrl="https://huggingface.co/Xenova/paraphrase-multilingual-MiniLM-L12-v2/resolve/main/onnx/model_quantized.onnx"
                DestinationFolder="$(IntermediateOutputPath)models/pmm12" />
  <!-- + sentencepiece.bpe.model (shared XLM-R tokenizer) -->
</Target>
<ItemGroup>
  <EmbeddedResource Include="$(IntermediateOutputPath)models/**/*" />
</ItemGroup>
```

### Zero-coupling invariant

The new components (`OnnxModel`, `OnnxModelRegistry`, `OnnxModelManifest`, the generators) reference
**no** type in `Prototype/` and never call `ModelManager`; `FromEmbeddedResources` only touches
`Assembly.GetManifestResourceStream`. They also reference **no** downloader type. The bytes may live in a
shared assembly and the downloader may fill a shared directory, but the *code* has no dependency on either
the legacy loader or the downloader package — only on a read stream and a directory layout.

### Replacement plan (when `HuggingFace.ModelDownloader` lands)

Only the **acquire** side and the composition root change; the two systems connect **solely through the
on-disk cache layout**, never a shared type:
- The downloader writes files into a cache dir (its own `CacheRoot`/manifest, its own types). Our
  `OnnxModelRegistry` reads that dir via `FromManifest` under its own convention
  (`<baseDirectory>/<key>/onnx/<Model>` + `<baseDirectory>/<key>/<asset>`). The **only** contract between
  them is that the produced files land where the registry looks — agreed at wiring time as a directory
  layout, not a shared manifest.
- `OnnxModelRegistry.FromConfiguration` / `AddOnnxModelRegistry` point `ModelsDirectory` at that dir;
  generators switch to the `(OnnxModelRegistry, options)` ctor keyed by `ModelId`.
- The build-time embed target + the `FromEmbeddedResources` call + `KontextModelsAssembly` are removed.
- **Unchanged:** `OnnxModel`, `OnnxModelRegistry`, `OnnxModelManifest`, all generators, `SentencePieceOnnxOptions`.

Because the downloader-era read path is local-dir (`FromManifest`) — different from the interim's embedded
read — the swap replaces a read source, not merely an acquirer. Contained (the generator is agnostic via
`OnnxModel`), but noted so it isn't mistaken for a drop-in.

## Decisions (resolved)

1. `Get` is read-only; no download — assets present up front. **(owner)**
2. Interim acquisition = build-time-download-and-embed (existing mechanism), in the existing
   `KurrentDB.Kontext.Models` assembly (can't move it now); models not committed to git. **(owner)**
3. Bridge read source = `OnnxModel.FromEmbeddedResources`, generic over `Assembly`, AOT-safe, zero legacy
   coupling. **(owner)**
4. Interim wiring uses the generator's `(OnnxModel, options)` ctor + `AddEmbeddingGenerator`; not the registry.
5. Assembly handle via a 1-line public marker type in `KurrentDB.Kontext.Models` + `typeof(...).Assembly`
   — reflection-free. The project is already edited to embed pMM12, so the marker is free; **no `Assembly.Load`**.
6. Interim production model = **pMM12** (`paraphrase-multilingual-MiniLM-L12-v2`), no input prefix. **(owner)**
7. Apply the `OnnxModel`/registry shape to **B (`WordPieceOnnx`)** too. **(owner)** — see below.
8. **No manifest coupling** to the downloader: the registry's `OnnxModelManifest` and the downloader's
   `ModelEntry` stay fully independent; the systems connect only via the cache-directory file layout. **(owner)**

## B (`WordPieceOnnx`) adoption — decided (7)

`WordPieceOnnx` currently takes raw `Stream`s (model + vocab). Give it the same treatment as C: take an
`OnnxModel` (+ registry ctor), reading `ReadModel()` and a `vocab` asset via `ReadAsset`. Its interim model
is the **already-embedded all-MiniLM** in `KurrentDB.Kontext.Models`, obtained through the same
`FromEmbeddedResources` path — so B and C share one bridge. Tracked as a follow-up implementation task.

## Alternatives Considered

- **Local-dir bridge (populate a `ModelsDirectory` now, read via `FromManifest`).** Cleaner in one respect
  — only *acquisition* is faked and no DLL bloat — but production wanted to lean on the existing
  build-time-embed pipeline in `KurrentDB.Kontext.Models` and ship the model inside the assembly with zero
  external file management for ~2 weeks. Revisit if the embedded model is large or multiplies.
- **Dedicated `KurrentDB.Kontext.Embeddings.Models` resource project.** Rejected for the interim — can't
  change where the models live now; reuse `KurrentDB.Kontext.Models` (code coupling is nil either way).
- **Lazy download-on-miss vs. eager warm-up** (for the *future* downloader). Out of scope (`Get` is
  read-only in the interim); flagged for the downloader wiring — leaning eager warm-up for fail-fast boot.
- **`OnnxSessionProvider`** (an earlier idea abstracting only the ONNX session). Rejected — it covered one
  asset and left the SentencePiece file as a raw stream; superseded by `OnnxModel` (model + assets).

## Open items (implementation)

- **Cache-directory layout contract** — when wiring the downloader, ensure its output lands where the
  registry reads (`<baseDirectory>/<key>/onnx/<Model>` + assets). This is the *only* seam between the two,
  and it's a filesystem convention, not a shared type.
- **pMM12 confirmation** — recommendation stands from a synthetic word-pair eval; a sentence-level
  recall@k eval on representative content should confirm before locking (see the sk-migration report).
