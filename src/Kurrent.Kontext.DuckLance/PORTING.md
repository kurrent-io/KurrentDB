# PORTING.md — DuckLance repo-extraction inventory

> **STATUS UPDATE (2026-07-18) — the source coupling described below has been ELIMINATED.**
> The two shared-source files this inventory identified (`Verify.cs`,
> `VectorStoreErrorHandler.cs`) are now **vendored inside the project** at
> `dotnet/src/VectorData/DuckLance/Internal/` (copied verbatim, original namespaces
> kept), and the `RestrictedInternalUtilities.props` import has been **removed** from
> `DuckLance.csproj`. Re-running the Section E verification confirms: **25 Compile
> items, all in-tree, ZERO out-of-tree** (was 21 in-tree + 24 out-of-tree). Copying the
> two project trees now requires **no source files from anywhere else in this repo** —
> only the Section B settings/pins replication still applies. The sections below are
> kept as the audit trail of how that conclusion was reached.

This document is a **verified, MSBuild-derived inventory** of everything in this repo
(`microsoft/semantic-kernel`) that `dotnet/src/VectorData/DuckLance` and
`dotnet/test/VectorData/DuckLance.Tests` depend on, so that copying those two project
trees into a standalone repo produces a self-contained, buildable result.

Everything below was produced with **evaluation-only** MSBuild invocations
(`-preprocess`, `-getItem`, `-getProperty`) — no `dotnet build`/`dotnet restore` was run
to produce this document (see Section E for the exact commands, which are safe to
re-run at any time, including while other builds are in flight, because they don't
write to `obj/`).

> Design context: the DuckLance README (`dotnet/src/VectorData/DuckLance/README.md`)
> states explicitly that it lives in this SK fork *only* to build against MEVD's
> in-tree abstractions and reuse `InternalUtilities`, and that the authoritative spec
> lives in the separate `kurrentdb` repo. This document is the "what exactly does that
> borrowing consist of" audit that a future extraction needs.

## Headline counts

- **SRC** (`DuckLance.csproj`): 147 distinct `.props`/`.targets` files get imported
  end-to-end (SDK internals + NuGet package build props + repo files + 2
  restore-generated `obj/*.nuget.g.*` files). Of those, only **5 are repo-owned,
  source-controlled files** the copy needs to care about (listed in Section B).
- **TESTS** (`DuckLance.Tests.csproj`): 173 distinct imports total; **5 repo-owned**
  source-controlled files (a different, parallel chain — see Section B).
- **Compile items**: SRC has 45 total Compile items — **21 in-tree** (`DuckLance/**/*.cs`)
    + **24 out-of-tree** (shared source pulled in via `RestrictedInternalUtilities.props`).
      Of those 24 out-of-tree files, only **2 are actually used** (`Verify.cs`,
      `VectorStoreErrorHandler.cs`); the other 22 are candidates for exclusion (see Section
      C for why — most of them compile to *nothing* on `net10.0`).
      TESTS has **30 in-tree, 0 out-of-tree** Compile items (the test project never imports
      `RestrictedInternalUtilities.props`, so it pulls in no shared source at all).
- **Surprising coupling found**: of the 22 unused out-of-tree files, **7 aren't merely
  unused — they compile to an *empty* translation unit on `net10.0`** because they're
  `#if !NETxxx_OR_GREATER`/`#if !NETCOREAPP` back-compat polyfills for the BCL types
  DuckLance's actual TFM already ships natively (`NotNull`/`MemberNotNull` attributes,
  `RequiresDynamicCode`, `RequiresUnreferencedCode`, `UnconditionalSuppressMessage`,
  `UnreachableException`, `IsExternalInit`, `CallerArgumentExpression`/
  `RequiredMember`/`CompilerFeatureRequired`, `ExperimentalAttribute`). A naive
  text-grep for e.g. `RequiresDynamicCode` in DuckLance source finds a hit — but the
  hit resolves to the **BCL type**, not the shared-source polyfill, because the
  polyfill's `#if` guard excludes it on `net10.0`. See Section C for the file-by-file
  proof.
- Also found: a live discrepancy between two consecutive `-getItem:Compile` snapshots
  of the test project during this audit (27 → 30 items) — three test files
  (`DependencyInjection/DuckLanceServiceCollectionExtensionsTests.cs`,
  `Sweeps/DuckDBConcurrentWritersTests.cs`, `Sweeps/DuckDBMultiVectorTests.cs`) were
  added mid-audit by concurrent work on this same tree. **This inventory reflects the
  second (later) snapshot** and the Compile-item lists in Section A/E are the ones to
  re-run, not to trust as frozen — see Section E.
- `dotnet/MEVD.slnx` **does** list both DuckLance projects (contrary to a naive
  assumption that it's unrelated) — but a `.slnx` is never an MSBuild `<Import>` target
  and doesn't appear anywhere in either preprocessed import chain, so it carries zero
  build inputs. It's IDE/dev-loop wiring only; not needed by a copy (Section C).
- A **security finding, out of repo scope**: `Kurrent.Quack` (a private/alpha package
  DuckLance genuinely uses) resolves through a GitHub Packages feed
  (`nuget.pkg.github.com/kurrent-io`) configured in the **user-level**
  `~/.nuget/NuGet/NuGet.Config` (not this repo's `dotnet/nuget.config`, which only
  allows nuget.org). That user-level file currently contains a **plaintext GitHub
  PAT** in `<packageSourceCredentials>`. This file is outside the repo and outside this
  audit's blast radius, so it was not modified, but it should be rotated/moved to a
  credential store — flagging here rather than reproducing the token value anywhere.

---

## A. Copy verbatim

### A.1 — The two project trees

Copy in full, as-is:

- `dotnet/src/VectorData/DuckLance/` — 21 `.cs` files + `DuckLance.csproj` +
  `AssemblyInfo.cs` + `README.md` (the README is user-facing docs for the connector
  itself and should travel with it — it documents the public API, platform support
  matrix, and operational notes that don't exist anywhere else).
- `dotnet/test/VectorData/DuckLance.Tests/` — 30 `.cs` files (see the live-churn note
  above; re-verify count at copy time) + `DuckLance.Tests.csproj` + the scoped
  `global.json` (already in-tree at
  `dotnet/test/VectorData/DuckLance.Tests/global.json`, opts this project into the
  Microsoft.Testing.Platform/TUnit runner — nothing further needed for that).

Recommended standalone-repo layout (assuming a repo root, not nested under `dotnet/`):

```
<new-repo>/
  src/DuckLance/                      <- contents of dotnet/src/VectorData/DuckLance/
    DuckLance.csproj
    AssemblyInfo.cs
    README.md
    DuckDBCollection.cs
    DuckDBVectorStore.cs
    DuckDBVectorStoreOptions.cs
    DuckLanceServiceCollectionExtensions.cs
    Filtering/  Indexing/  Mapping/  Schema/  Search/  Storage/
    Internal/                         <- NEW: the 2 shared-source files (see A.2)
  test/DuckLance.Tests/               <- contents of dotnet/test/VectorData/DuckLance.Tests/
    DuckLance.Tests.csproj
    global.json
    Crud/ DependencyInjection/ Embeddings/ Filtering/ Hardening/ Indexing/
    Lifecycle/ Mapping/ Schema/ Search/ Storage/ Support/ Sweeps/ LanceSmokeTests.cs
    DuckDBVectorStoreTests.cs
  Directory.Build.props               <- consolidated snippet, Section B
  Directory.Packages.props            <- optional, Section B (or drop CPM entirely)
  nuget.config                        <- Section D (needs the kurrent-io feed)
  global.json                         <- Section B (SDK pin)
  .editorconfig                       <- optional, copy repo-root .editorconfig verbatim
```

### A.2 — Out-of-tree compiled files that are actually USED

Only **2 of the 24** out-of-tree `Compile` items DuckLance's `.csproj` pulls in via
`RestrictedInternalUtilities.props` are referenced by DuckLance's own code (confirmed
by grep across all 21 in-tree `.cs` files, tracking both type-name references and
extension-method call sites, not just import statements):

| Source path (this repo)                                            | Link path (in build)                     | Used by                                                          |
|--------------------------------------------------------------------|------------------------------------------|------------------------------------------------------------------|
| `dotnet/src/InternalUtilities/src/Diagnostics/Verify.cs`           | `Shared/Diagnostics/Verify.cs`           | `DuckDBCollection.cs`, `DuckLanceServiceCollectionExtensions.cs` |
| `dotnet/src/InternalUtilities/src/Data/VectorStoreErrorHandler.cs` | `Shared/Data/VectorStoreErrorHandler.cs` | `DuckDBCollection.cs`, `DuckDBVectorStore.cs`                    |

Both files are **not** TFM-guarded away on `net10.0` — each has an `#if NET ... #else
... #endif` split where the `#if NET` branch (the modern-BCL implementation) is what
actually compiles for DuckLance's `net10.0`-only target; the `#else` branch is dead
code for this project but doesn't prevent the type from being declared.

**No further cascading dependencies.** Checked both files' `using` directives
directly (not just their `#if` guards): every `using` in both `Verify.cs` (186 lines)
and `VectorStoreErrorHandler.cs` (468 lines) resolves to a BCL namespace only —
`System`, `System.Collections.Generic`, `System.Diagnostics`,
`System.Diagnostics.CodeAnalysis`, `System.IO`, `System.Linq`,
`System.Runtime.CompilerServices`, `System.Text.RegularExpressions`,
`System.Data.Common`, `System.Threading`, `System.Threading.Tasks`. Neither file
`using`s or otherwise references any other file in `InternalUtilities/` (not
`HttpClientProvider`, not `PathUtilities`, none of the other 22 shared-source
candidates). So the two used files are a closed set — copying exactly these two,
and nothing else from `InternalUtilities/`, is sufficient; no third file gets pulled
in transitively behind them.

Copy these two files into `src/DuckLance/Internal/` (or equivalent) in the new repo,
**keeping their `// Copyright (c) Microsoft. All rights reserved.` header and their
existing `namespace Microsoft.Extensions.VectorData;` / `namespace
Microsoft.SemanticKernel;` declarations** — DuckLance's `.cs` files rely on those
namespaces being in scope (`using Microsoft.SemanticKernel;` for `Verify`, and
`Microsoft.Extensions.VectorData` for `VectorStoreErrorHandler`, which DuckLance is
already implicitly in via other MEVD `using`s). Do not rename the namespaces — that
would require touching every DuckLance call site.

Do **not** add a `Link`-based reference back into `InternalUtilities/` from the new
repo; that directory doesn't exist there. Just take physical copies.

---

## B. Replicate as settings, don't copy

DuckLance never `<Import>`s the *whole* `dotnet/Directory.Build.props` tree from a
standalone repo's perspective — copying these files verbatim would drag in ~700 lines
of unrelated SK-wide policy (all connectors' `NoWarn`s, AOT settings, Aspire package
versions, etc.). Instead, replicate only what actually reaches DuckLance, confirmed by
walking the real import chain in the preprocessed output.

### B.1 — Confirmed import chain (in order)

**SRC** (`DuckLance.csproj`, found via `-preprocess`):

1. `dotnet/src/InternalUtilities/src/RestrictedInternalUtilities.props` — explicit
   `<Import>` in the csproj itself. Contributes the 24 shared-source `Compile` items
   (Section A.2 covers which 2 matter).
2. `dotnet/src/VectorData/Directory.Build.props` — found implicitly (nearest
   `Directory.Build.props` walking up from the project directory). Contributes:
   `NoWarn` += `MEVD9000,MEVD9001` (suppresses "you're using an experimental MEVD
   API" warnings — DuckLance itself doesn't declare `[Experimental]` on anything, it
   just *consumes* MEVD's experimental surface).
3. `dotnet/Directory.Build.props` — imported *by* file #2 above (`<Import
   Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props',
   '$(MSBuildThisFileDirectory)../'))" />`). Contributes the properties in B.2 below.
4. `dotnet/Directory.Build.targets` — found implicitly (SDK auto-imports the nearest
   `Directory.Build.targets`; there is exactly one in the whole `dotnet/` tree).
   Contributes: enables the old-style `Microsoft.Build.CentralPackageVersions` NuGet
   Sdk (v2.1.3) — redundant with the also-enabled built-in
   `ManagePackageVersionsCentrally` in `Directory.Packages.props`, safe to ignore in a
   new repo — and a `DotnetFormatOnBuild` target that shells out to `dotnet format`
   on local `Release` builds only (skipped when `$(GITHUB_ACTIONS)` or `$(TF_BUILD)`
   is set). Optional to replicate; it's a dev-convenience hook, not a correctness
   dependency.
5. `dotnet/Directory.Packages.props` — via NuGet's `Sdk="Microsoft.Build.NuGetSdkResolver"`
   auto-import triggered by `ManagePackageVersionsCentrally=true`. Contributes package
   version pins (Section B.4).

**TESTS** (`DuckLance.Tests.csproj`) — a *parallel, different* 3-level chain (found via
its own `-preprocess`; note it does **not** go through `dotnet/src/VectorData/`):

1. `dotnet/test/VectorData/Directory.Build.props` — nearest `Directory.Build.props`
   above the test project. Contributes `NoWarn` +=
   `MEVD9000,MEVD9001;CA1515;CA1707;CA1716;CA1720;CA1721;CA1819;CS1819;CA1861;CA1863;CA2007;VSTHRD111;CS1591;IDE1006;IDE0340`
   (all test-code-specific analyzer relaxations — internal types OK, no XML docs
   required, `ConfigureAwait` not required in tests, etc.).
2. `dotnet/test/Directory.Build.props` — imported by #1 (same
   `GetPathOfFileAbove(...'../')` pattern, one level up). Contributes `NoWarn` +=
   `Moq1400` (irrelevant to DuckLance.Tests, which doesn't use Moq, but harmless).
3. `dotnet/Directory.Build.props` — imported by #2. Same as SRC's #3 above.
4. `dotnet/Directory.Build.targets` and `dotnet/Directory.Packages.props` — same as
   SRC's #4/#5.

The test project does **not** import `RestrictedInternalUtilities.props` — confirmed
by `-getItem:Compile` returning zero out-of-tree items.

### B.2 — Resolved effective properties (queried directly via `-getProperty`, not eyeballed)

| Property                    | SRC effective value                                                                                                                                                                                       | TESTS effective value                                                                                                                               | Source                         |
|-----------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------|
| `TargetFramework`           | `net10.0`                                                                                                                                                                                                 | `net10.0`                                                                                                                                           | each csproj                    |
| `LangVersion`               | `14`                                                                                                                                                                                                      | `14`                                                                                                                                                | `dotnet/Directory.Build.props` |
| `Nullable`                  | `enable`                                                                                                                                                                                                  | `enable`                                                                                                                                            | `dotnet/Directory.Build.props` |
| `ImplicitUsings`            | `disable`                                                                                                                                                                                                 | `disable`                                                                                                                                           | `dotnet/Directory.Build.props` |
| `AnalysisMode`              | `AllEnabledByDefault`                                                                                                                                                                                     | (same)                                                                                                                                              | `dotnet/Directory.Build.props` |
| `AnalysisLevel`             | `latest`                                                                                                                                                                                                  | (same)                                                                                                                                              | `dotnet/Directory.Build.props` |
| `RunAnalyzersDuringBuild`   | `true`                                                                                                                                                                                                    | (same)                                                                                                                                              | `dotnet/Directory.Build.props` |
| `EnableNETAnalyzers`        | evaluates `true` from Directory.Build.props, but ends up **`false`** at build time                                                                                                                        | (same)                                                                                                                                              | See note below                 |
| `GenerateDocumentationFile` | `true`                                                                                                                                                                                                    | n/a (not packed)                                                                                                                                    | `dotnet/Directory.Build.props` |
| `IsPackable`                | `true` (csproj's own `<IsPackable>true</IsPackable>` at the top of the file wins — it's evaluated *after* Directory.Build.props's `disable` default, so last-write-wins in document order)                | `false` (csproj explicit)                                                                                                                           | csproj                         |
| `NoWarn` (final)            | `;IDE0290;IDE0079;MEVD9000,MEVD9001`                                                                                                                                                                      | `;IDE0290;IDE0079;Moq1400;MEVD9000,MEVD9001;CA1515;CA1707;CA1716;CA1720;CA1721;CA1819;CS1819;CA1861;CA1863;CA2007;VSTHRD111;CS1591;IDE1006;IDE0340` | chain above                    |
| `RepoRoot`                  | computed via `GetPathOfFileAbove('.gitignore', ...)` — used only by `RestrictedInternalUtilities.props` to locate `InternalUtilities/`; irrelevant once those 2 files are physically copied (Section A.2) | —                                                                                                                                                   | `dotnet/Directory.Build.props` |

**`EnableNETAnalyzers` note**: `dotnet/Directory.Build.props` sets it `true`, but the
NuGet package `Microsoft.CodeAnalysis.NetAnalyzers` (pulled in transitively via one of
DuckLance's `PackageReference`s) ships a `buildTransitive\DisableNETAnalyzersForNuGetPackage.props`
that force-sets it back to `false` — this is standard, intentional .NET SDK behavior
(avoids double-running the same CA rules from both the SDK-bundled analyzer and the
NuGet-delivered one); the analyzers still run, just sourced from the NuGet package.
Nothing to configure for this in the new repo — it happens automatically as long as
the same package graph is restored.

Also: `dotnet/Directory.Build.props` injects one assembly-level attribute
(`[assembly: CLSCompliant(false)]`) via `<AssemblyAttribute>`. Cosmetic; include only
if you want exact parity.

### B.3 — Ready-to-paste `Directory.Build.props` for the standalone repo

```xml
<Project>
  <PropertyGroup>
    <LangVersion>14</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>

    <RunAnalyzersDuringBuild>true</RunAnalyzersDuringBuild>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisMode>AllEnabledByDefault</AnalysisMode>
    <AnalysisLevel>latest</AnalysisLevel>

    <GenerateDocumentationFile>true</GenerateDocumentationFile>

    <!-- MEVD's connector-facing surface is [Experimental]; DuckLance consumes it. -->
    <NoWarn>$(NoWarn);IDE0290;IDE0079;MEVD9000;MEVD9001</NoWarn>
  </PropertyGroup>

  <!-- Only if/when a test project is added under this same tree: -->
  <PropertyGroup Condition="'$(MSBuildProjectName)' == 'DuckLance.Tests'">
    <NoWarn>$(NoWarn);CA1515;CA1707;CA1716;CA1720;CA1721;CA1819;CS1819;CA1861;CA1863;CA2007;VSTHRD111;CS1591;IDE1006;IDE0340</NoWarn>
  </PropertyGroup>
</Project>
```

(`IsPackable` is intentionally omitted here — each project already sets it explicitly
in its own `.csproj`, so there's no ambient default to replicate.)

### B.4 — Package pins

DuckLance's `.csproj` files use bare `<PackageReference Include="X" />` (no
`Version="..."`) because this repo uses NuGet Central Package Management
(`ManagePackageVersionsCentrally=true` in `dotnet/Directory.Packages.props`), with two
of the six packages **overridden per-project** via `VersionOverride="10.7.0"` (bumping
past the repo-wide CPM pin, which is lower — `10.5.0`/`10.1.0` respectively — because
DuckLance needs a newer MEVD/M.E.AI surface than the rest of the repo has moved to
yet). In a standalone repo with no repo-wide CPM baseline to override, the
`VersionOverride` value simply **becomes the one true pin** — there's nothing left to
"override".

Resolved pins (verified against `dotnet/Directory.Packages.props`, cross-checked
against each `VersionOverride` in the two `.csproj`s):

| Package                                                 | Version to pin    | Used by    | Note                                                                               |
|---------------------------------------------------------|-------------------|------------|------------------------------------------------------------------------------------|
| `Microsoft.Extensions.DependencyInjection.Abstractions` | `10.0.6`          | SRC        | plain CPM pin, no override                                                         |
| `Microsoft.Extensions.AI.Abstractions`                  | **`10.7.0`**      | SRC, TESTS | was `VersionOverride="10.7.0"` in both csprojs; repo-wide CPM baseline is `10.5.0` |
| `Microsoft.Extensions.VectorData.Abstractions`          | **`10.7.0`**      | SRC, TESTS | was `VersionOverride="10.7.0"` in both csprojs; repo-wide CPM baseline is `10.1.0` |
| `Microsoft.Extensions.Logging.Abstractions`             | `10.0.6`          | SRC        | plain CPM pin                                                                      |
| `DuckDB.NET.Data.Full`                                  | `1.5.3`           | SRC, TESTS | plain CPM pin                                                                      |
| `Kurrent.Quack`                                         | `0.0.0-alpha.181` | SRC        | plain CPM pin; **private feed**, see Section D                                     |
| `TUnit`                                                 | `1.61.0`          | TESTS      | plain CPM pin                                                                      |
| `Microsoft.SemanticKernel.Connectors.Onnx`              | `1.78.0-alpha`    | TESTS      | plain CPM pin; public, resolves from nuget.org                                     |

Either drop CPM entirely for the new repo (simplest — put `Version="..."` directly on
each `<PackageReference>` using the table above) or keep a minimal
`Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.6" />
    <PackageVersion Include="Microsoft.Extensions.AI.Abstractions" Version="10.7.0" />
    <PackageVersion Include="Microsoft.Extensions.VectorData.Abstractions" Version="10.7.0" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.6" />
    <PackageVersion Include="DuckDB.NET.Data.Full" Version="1.5.3" />
    <PackageVersion Include="Kurrent.Quack" Version="0.0.0-alpha.181" />
    <PackageVersion Include="TUnit" Version="1.61.0" />
    <PackageVersion Include="Microsoft.SemanticKernel.Connectors.Onnx" Version="1.78.0-alpha" />
  </ItemGroup>
</Project>
```

If you keep CPM, both `.csproj`s can then drop their `VersionOverride="10.7.0"`
attributes (there's no repo-wide baseline left for them to override).

### B.5 — `.editorconfig` essentials

Simplest option: copy the repo-root `.editorconfig` verbatim (it's a plain text file,
no MSBuild coupling). If trimming it down instead, the rules that materially affect
whether DuckLance's existing code compiles clean under the same settings are:

- `[*.{cs,csx,vb,vbx}] charset = utf-8-bom` — DuckLance's `.cs` files are saved with a
  UTF-8 BOM; `dotnet format`/IDEs will rewrite them without this rule, causing pointless
  diff noise (and CI's `dotnet format --verify-no-changes`-equivalent, if replicated,
  would flag it).
- `file_header_template = Copyright (c) Microsoft. All rights reserved.` — every
  DuckLance file currently starts with exactly this line. **Decide up front** whether
  the new repo keeps "Copyright (c) Microsoft" (likely wrong for a Kurrent-owned repo)
  or changes it to something else — either way, update this editorconfig rule *and*
  every file's first line together, in one pass, rather than drifting.
- `dotnet_style_qualification_for_{field,property,method,event} = true:error` — the
  existing code uses `this.` qualification throughout; if the new repo's CI runs
  `--warnaserror` (this repo's does), dropping this rule would make already-compliant
  code merely "unenforced" (harmless), but re-adding `this.`-qualification rules later
  against code that grew without them would be a large mechanical diff — keep it from
  day one if you want the option preserved.
- The five `severity = error` naming-convention rules (constant/local-constant PascalCase,
  private-field/private-static-field underscore prefix, async-method `Async` suffix) —
  DuckLance's field names already conform; only matters if CI treats analyzer output as
  build-breaking.

### B.6 — SDK pin guidance

`dotnet/global.json` (repo root, above `DuckLance/`) pins:

```json
{ "sdk": { "version": "10.0.301", "rollForward": "major", "allowPrerelease": false } }
```

Two things worth knowing before copying this pattern:

1. **`rollForward: "major"` is permissive**, not defensive — during this very audit,
   running `dotnet msbuild` from the *repo root* (one level above where `global.json`
   actually lives, at `dotnet/global.json`) meant the SDK resolver never found that
   `global.json` at all (it only searches upward from the current working directory,
   never into subdirectories) and silently picked up whatever the newest installed SDK
   was — a `11.0.100-preview.5` build on this machine — instead of erroring. Put
   `global.json` at the actual repo root of the new repo, and always invoke `dotnet`/
   `dotnet msbuild` from inside the repo (or a subdirectory of it), not above it.
2. The test project's own nested `global.json`
   (`dotnet/test/VectorData/DuckLance.Tests/global.json`) is unrelated to SDK pinning —
   it *also* repeats the same `"sdk"` block (redundant but harmless) and additionally
   sets `"test": { "runner": "Microsoft.Testing.Platform" }`, which is what makes
   `dotnet test`/`dotnet run` use TUnit's Microsoft.Testing.Platform protocol instead
   of the legacy VSTest protocol for this project. This file is already copied
   verbatim as part of Section A — nothing to hand-author here.

---

## C. Explicitly NOT needed

Confirmed by grepping both preprocessed outputs (`-preprocess`) for every occurrence of
each name — none appear anywhere in either project's import chain:

- `dotnet/nuget/nuget-package.props` — never imported by either `.csproj` (this repo's
  own package-metadata defaults — `Authors`, `PackageLicenseExpression`, icon, etc. —
  are for the `Microsoft.SemanticKernel.*` package family; DuckLance's `.csproj` sets
  its own `PackageId`/`Title`/`Description` directly and never opts into this file).
- `dotnet/nuget/icon.png`, `dotnet/nuget/NUGET.md`,
  `dotnet/nuget/VECTORDATA-CONNECTORS-NUGET.md` — same reasoning; only reachable
  through `nuget-package.props`, which DuckLance never imports.
- `dotnet/MEVD.slnx` — **does** list both DuckLance projects (`src/VectorData/DuckLance/DuckLance.csproj`
  and `test/VectorData/DuckLance.Tests/DuckLance.Tests.csproj`), so it's not
  "unrelated" — but a `.slnx`/`.sln` file is never an MSBuild `<Import>` target. It
  doesn't appear anywhere in either preprocessed import chain and contributes zero
  build inputs, properties, or items. It's IDE/`dotnet test`-at-the-solution-level
  convenience only; the new repo needs its own (much smaller) solution or solution
  filter if desired, but doesn't need this file.
- `dotnet/SK-dotnet.slnx` — doesn't reference DuckLance at all (confirmed by grep).
- 22 of the 24 out-of-tree `Compile` items pulled in via `RestrictedInternalUtilities.props`
  (everything except `Verify.cs` and `VectorStoreErrorHandler.cs` — see Section A.2).
  Seven of those 22 are not just unreferenced but **compile to a fully empty file** on
  `net10.0` because of their own `#if` TFM guards:

  | File | Guard | True on net10.0? |
    |---|---|---|
  | `Diagnostics/CompilerServicesAttributes.cs` | `#if !NETCOREAPP` | No → empty |
  | `Diagnostics/DynamicallyAccessedMembersAttribute.cs` | `#if !NET5_0_OR_GREATER` | No → empty |
  | `Diagnostics/ExperimentalAttribute.cs` | `#if !NET8_0_OR_GREATER` | No → empty (and DuckLance never applies `[Experimental]` to its own API anyway) |
  | `Diagnostics/IsExternalInit.cs` | `#if !NET8_0_OR_GREATER` | No → empty |
  | `Diagnostics/NullableAttributes.cs` | `#if !NETCOREAPP && !NETSTANDARD2_1` / `#if !NETCOREAPP \|\| NETCOREAPP3_1` | No (both blocks) → empty |
  | `Diagnostics/RequiresDynamicCodeAttribute.cs` | `#if !NET7_0_OR_GREATER` | No → empty |
  | `Diagnostics/RequiresUnreferencedCodeAttribute.cs` | `#if !NET5_0_OR_GREATER` | No → empty |
  | `Diagnostics/UnconditionalSuppressMessageAttribute.cs` | `#if !NET8_0_OR_GREATER` | No → empty |
  | `Diagnostics/UnreachableException.cs` | `#if !NET8_0_OR_GREATER` | No → empty |
  | `Http/HttpContentPolyfills.cs` | `#if !NET5_0_OR_GREATER` | No → empty |
  | `System/ValueTaskExtensions.cs` | `#if !NETCOREAPP` | No → empty |

  These exist in `InternalUtilities` to support SK's *other* projects, which
  multi-target down to `netstandard2.0`/`net472` (per this repo's CLAUDE.md); DuckLance
  targets `net10.0` only, so every one of these is dead weight even if a future
  maintainer's editor shows the type as "used" (it resolves to the BCL, not the shared
  file). Caution: if the new repo ever adds a lower TFM to DuckLance, re-run the check
  in Section E — the calculus changes.

  The remaining unused-and-not-guarded files (genuinely unused utility code, not
  TFM-excluded, confirmed via both type-name grep and extension-method-name grep so
  extension-method call sites weren't missed): `Http/HttpClientProvider.cs`,
  `Http/HttpHeaderConstant.cs`, `Linq/EnumerableExtensions.cs`,
  `System/AppContextSwitchHelper.cs`, `System/EmptyKeyedServiceProvider.cs`,
  `System/EnvExtensions.cs`, `System/IListExtensions.cs`,
  `System/InternalTypeConverter.cs`, `System/NonNullCollection.cs`,
  `System/PathUtilities.cs`, `System/TypeConverterFactory.cs`.

---

## D. Environment prerequisites

Not repo files — external state the new repo's dev machines/CI need, discovered from
source (`Storage/DuckDBConnectionManager.cs`, `Support/EmbeddingModelFixture.cs`,
`Support/LanceRequiredAttribute.cs`, `README.md`):

- **Network + DuckDB extension cache for `lance`.** `DuckDBConnectionManager` runs
  `INSTALL lance; LOAD lance;` on first connection unless
  `DuckDBVectorStoreOptions.ExtensionPath` points at a pre-downloaded
  `.duckdb_extension` file (the air-gapped path). DuckDB caches installed extensions in
  its own default extension directory (`~/.duckdb/extensions/` unless overridden by
  DuckDB-level configuration) — this is DuckDB's own cache, not something DuckLance
  manages.
- **Platform support is a hard subset.** Per `README.md` and
  `Support/LanceRequiredAttribute.cs`, the `lance` extension ships binaries only for
  linux-x64, linux-arm64, osx-arm64, and win-x64 — **not osx-x64 (Intel Mac)**. Tests
  tagged `[LanceRequired]` self-skip (via a TUnit `SkipAttribute`) on unsupported
  platforms rather than failing; CI matrices for the new repo should account for this
  gap explicitly rather than discovering it from red builds.
- **ONNX embedding model download + cache.** `Support/EmbeddingModelFixture.cs`
  downloads `TaylorAI/bge-micro-v2` (`model.onnx` + `vocab.txt`, 384-dim) from
  `huggingface.co` on first use per process, caching under
  `~/.cache/ducklance-tests/bge-micro-v2/`. It's offline-tolerant by design (falls back
  to "unavailable", tests using it should skip rather than fail) but a from-scratch CI
  runner will pay a one-time download on its first embedding-generation test run unless
  that cache directory is pre-seeded/persisted between runs.
- **Private package feed for `Kurrent.Quack`.** This repo's own `dotnet/nuget.config`
  locks package sources to `nuget.org` only (`<clear />` + a single `nuget.org` source
  with package-source-mapping `pattern="*"`). `Kurrent.Quack` is **not** resolvable from
  that config — it currently resolves only because the *user-level*
  `~/.nuget/NuGet/NuGet.Config` on this machine adds a second source
  (`https://nuget.pkg.github.com/kurrent-io/index.json`) with stored credentials. The
  new standalone repo needs its own equivalent: a `nuget.config` (or CI-level NuGet
  source config) that adds the `kurrent-io` GitHub Packages feed, likely with a package
  source mapping entry for `Kurrent.*` (package-source-mapping is strict — adding the
  source without a mapping rule will still fail to resolve `Kurrent.Quack` if mapping is
  enabled), and a non-plaintext credential mechanism (GitHub Actions `secrets.*`, a
  credential provider, or `dotnet nuget add source --username --password
  --store-password-in-clear-text false`) rather than the plaintext PAT currently
  sitting in this machine's user-level config.
- **`Microsoft.SemanticKernel.Connectors.Onnx` is public**, unlike `Kurrent.Quack` — it
  resolves from plain `nuget.org`, confirmed by `dotnet/nuget.config` listing only
  `nuget.org` and this package genuinely restoring in this environment. No special feed
  needed for it.

---

## E. Verification recipe

Re-run these from the **repo root** of `microsoft/semantic-kernel`, with working
directory inside `dotnet/` (or pass an explicit `-p:...` SDK override) so
`dotnet/global.json`'s SDK pin is actually honored — see the B.6 gotcha. All of these
are evaluation-only: they don't touch `obj/`, don't restore, and are safe to run
alongside concurrent `dotnet build`/`dotnet test` invocations elsewhere in the repo.

```bash
cd dotnet   # so global.json's SDK pin (10.0.301) is honored

# 1. Full import chain, in order — grep the "====...====" delimited comment blocks
#    for "<Import Project=" and the file path on the line right before the closing
#    "====" to reconstruct the chain by hand, or just grep for repo-owned paths:
dotnet msbuild src/VectorData/DuckLance/DuckLance.csproj \
  -preprocess:/tmp/ducklance-src-pp.xml
grep -oE '^/Users/[^ ]+\.(props|targets)$' /tmp/ducklance-src-pp.xml | sort -u

dotnet msbuild test/VectorData/DuckLance.Tests/DuckLance.Tests.csproj \
  -preprocess:/tmp/ducklance-tests-pp.xml
grep -oE '^/Users/[^ ]+\.(props|targets)$' /tmp/ducklance-tests-pp.xml | sort -u

# 2. Compile item lists (re-run this one often — see the live-churn note in
#    "Headline counts"; it reflects the current state of the working tree, not a
#    snapshot):
dotnet msbuild src/VectorData/DuckLance/DuckLance.csproj -getItem:Compile
dotnet msbuild test/VectorData/DuckLance.Tests/DuckLance.Tests.csproj -getItem:Compile
# Filter for FullPath not starting with the DuckLance/DuckLance.Tests directory itself
# to isolate the out-of-tree ("Shared/...") items and their "Link" metadata.

# 3. Resolved effective properties (Section B.2):
dotnet msbuild src/VectorData/DuckLance/DuckLance.csproj \
  -getProperty:LangVersion -getProperty:Nullable -getProperty:ImplicitUsings \
  -getProperty:NoWarn -getProperty:IsPackable -getProperty:RepoRoot \
  -getProperty:AnalysisMode -getProperty:AnalysisLevel -getProperty:EnableNETAnalyzers \
  -getProperty:RunAnalyzersDuringBuild -getProperty:TargetFramework \
  -getProperty:GenerateDocumentationFile

dotnet msbuild test/VectorData/DuckLance.Tests/DuckLance.Tests.csproj \
  -getProperty:NoWarn -getProperty:LangVersion -getProperty:Nullable \
  -getProperty:TargetFramework -getProperty:OutputType \
  -getProperty:TestingPlatformDotnetTestSupport -getProperty:IsPackable

# 4. InternalsVisibleTo (Section A/general):
dotnet msbuild src/VectorData/DuckLance/DuckLance.csproj -getItem:InternalsVisibleTo

# 5. Re-check "used vs merely-compiled" for the out-of-tree files (Section A.2/C) —
#    from dotnet/src/VectorData/DuckLance/, grep the type/member names each shared
#    file declares against all in-tree *.cs (see git history of this file for the
#    exact name list used); re-verify the #if guards haven't changed if DuckLance's
#    TargetFramework(s) change:
grep -n '#if' ../InternalUtilities/src/Diagnostics/*.cs ../InternalUtilities/src/Http/*.cs \
  ../InternalUtilities/src/System/*.cs ../InternalUtilities/src/Data/*.cs \
  ../InternalUtilities/src/Linq/*.cs
```
