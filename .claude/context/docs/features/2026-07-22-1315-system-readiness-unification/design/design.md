---
title: System-readiness component unification
status: settling
authors: [sergio]
date: 2026-07-22
tags: [core, hosting, connectors, schema-registry, kontext]
---

# Design Space — System-readiness component unification

## Problem / Trigger

The node-readiness plumbing had been copy-pasted into three modules. Kontext became the third copy,
and SchemaRegistry's own source admitted the debt:

```
// TODO: Refactor to ensure both connector and registry use a unified system readiness component
```

## Exploration

### What was actually duplicated

Diffing the three copies (namespace-normalized) showed far less divergence than the copy count implied:

| File | Connectors vs SchemaRegistry | SchemaRegistry vs Kontext |
|---|---|---|
| `NodeSystemInfo.cs` | byte-identical | byte-identical |
| `NodeSystemInfoWireUpExtensions.cs` | byte-identical | byte-identical |
| `NodeSystemInfoProvider.cs` | one `using` | one `using` |
| `SystemReadinessProbe.cs` | one `using` + a dead comment block | one `using` |
| `NodeBackgroundService.cs` | one `using` + one `.ConfigureAwait(false)` | one `using` |
| `SystemStartupTasks.cs` | **real divergence** (see below) | identical |

Kontext's copies were verbatim SchemaRegistry.

### The live surface was much smaller than the copy count

| Component | Live consumers |
|---|---|
| `NodeBackgroundService` | SchemaRegistry's projector; Connectors' `LeaderNodeBackgroundService` → `ConnectorsControlService` |
| `SystemReadinessProbe` + `NodeSystemInfo` | **Connectors startup tasks only** |
| `SystemStartupTasks` | **Connectors only** (2 tasks, driven by `SystemStartupManager`) |
| `SystemBackgroundService` | **none — zero subclasses, dead code** |
| `DuckDBProjectorBackgroundService` (`SystemReady` gate) | SchemaRegistry's projector |

This collapsed the framing of "two incompatible readiness flavors competing for the same consumers".
`SystemBackgroundService` — the class that looked like *the* Connectors readiness gate — has no
subclasses at all, and SchemaRegistry's and Kontext's probe / `NodeSystemInfo` / startup-task copies
were entirely unused. There is exactly one live probe consumer (Connectors) and one live `SystemReady`
consumer (SchemaRegistry). They never competed.

### `SystemStartupTasks` divergence

- **Connectors (live)**: `SystemStartupTaskService` is a plain class with a public `ExecuteAsync`,
  registered as `AddSingleton<SystemStartupTaskWorker>`. `SystemStartupManager` resolves the workers
  via `GetServices<SystemStartupTaskWorker>()` and drives them itself with a 30s budget.
- **SchemaRegistry / Kontext (dead)**: the same service extends `BackgroundService` and is registered
  as `IHostedService`.

The dead variant could not be the one that moves — adopting it would have broken Connectors' startup
manager, which is the only caller.

### Home candidates

- **`KurrentDB.Core`** — owns `SystemMessage`; reachable from all three (Connectors and Kontext
  directly, SchemaRegistry transitively via `KurrentDB.Surge`). Connectors references Core as
  `ExcludeAssets="all" Private="false"` (compile-only, host-provided), so Core costs no new shipped
  assembly. Requires dropping the two `Kurrent.Surge` helpers (`.With`, `.Then`) because Surge depends
  on Core, not the reverse.
- **New shared project** — cleaner layering, but a new assembly every plugin must package and ship.
- **`KurrentDB.Surge`** — Kontext doesn't reference it (uses the `Kurrent.Surge.Core` package), and it
  makes a messaging library the owner of node hosting.

## Decisions

- 2026-07-22 — **Home is `KurrentDB.Core`**, over a new shared project and over `KurrentDB.Surge`.
  Decisive factor: Connectors deliberately ships no Core assembly (compile-only reference), so a new
  shared project would have to be packaged into each plugin, whereas Core is already host-provided.
- 2026-07-22 — **One flat folder, `src/KurrentDB.Core/Hosting/`, namespace `KurrentDB.Core.Hosting`.**
  Flattening removes the `NodeSystemInfo.NodeSystemInfo` double-qualification wart the old
  type-inside-same-named-namespace layout forced on every consumer signature.
- 2026-07-22 — **The projector gate stays out of scope.** `DuckDBProjectorBackgroundService` (the
  `SystemReady` flavor) is a projector part and was left where it is. No consumer's readiness
  semantics changed: SchemaRegistry still waits on `SystemReady`, Connectors' startup tasks still wait
  on the role-aware probe. Collapsing the two signals remains open.
- 2026-07-22 — **`SystemStartupTasks` unifies on Connectors' live variant** (plain class + worker
  registration), because the `IHostedService` variant in SchemaRegistry/Kontext had no callers and
  adopting it would break `SystemStartupManager`.
- 2026-07-22 — **`SystemStartupTaskService` and `SystemStartupTaskWorker` became `public`.** They were
  assembly-internal in Connectors; crossing the assembly boundary into Core requires it.
- 2026-07-22 — **`.With` / `.Then` inlined**, not ported. They are two `Kurrent.Surge` conveniences;
  Core sits below Surge so it cannot take that dependency.
- 2026-07-22 — Moved files adopt **tabs**, matching `src/.editorconfig` (`indent_style = tab`) and
  Core's existing sources. The three origin modules used 4 spaces, deviating from the repo config.
- 2026-07-22 — **History is preserved by splitting the work into two commits.** Reindenting inside
  the move would have destroyed rename detection: measured similarity fell to 11–25% for reformatted
  files, versus 75–84% for the two moved without reformatting — below git's default 50% threshold, so
  they would have committed as delete + create and `git log --follow` would dead-end. Fixed by
  committing the move and adaptation first with the original indentation intact (all 12 files detected
  as renames, lowest 64%), then the tab conversion as a separate whitespace-only commit
  (`git diff -w HEAD~1 HEAD` is empty). Both commits build and are test-green.

  Lesson worth keeping: **never reformat inside a move commit.** Git does not store renames, it infers
  them from content similarity at diff time, so a reindent and a move in one commit are
  indistinguishable from a delete plus an unrelated new file.
- 2026-07-22 — Files opt into NRT per-file with `#nullable enable`, matching the 46 Core files that
  already do. Core enables neither `ImplicitUsings` nor `Nullable` project-wide, so the moved files
  also carry explicit `using System...` directives.

### Round two — the leader lineage and the rest of the plumbing

Sérgio's follow-up: the leader lineage, `SystemBackgroundService`, `ISystemReadinessProbe` and
`SystemStartupManager` should move too, so they can be reused.

Four of those needed no debate — `ISystemReadinessProbe` had already ridden along inside
`SystemReadinessProbe.cs`, and `SystemBackgroundService`, `SystemStartupManager` and its
`IStartupWorkCompletionMonitor` have no Surge coupling. The leader lineage did:

| Component | Surge coupling |
|---|---|
| `NodeLifetimeService` | `Kurrent.Surge.TokenCompletionSource` |
| `LeaderNodeBackgroundService` | constructs `NodeLifetimeService` |
| `LeaderNodeProcessorWorker<T>` | `where T : IProcessor` (`Kurrent.Surge.Processors`) |

`TokenCompletionSource` was verified to live in the `Kurrent.Surge.Core` **package** — a binary scan
found 0 occurrences in `DotNext.Threading` and the compiler rejected it under `using DotNext.Threading`.
Core sits below Surge, so none of the three could move to Core unchanged.

- 2026-07-22 — **Split the lineage across two homes** (Sérgio's ruling): `NodeLifetimeService` plus a
  Core-owned `TokenCompletionSource` go to `KurrentDB.Core.Hosting`; `LeaderNodeBackgroundService` and
  `LeaderNodeProcessorWorker<T>` go to a new `KurrentDB.Surge.Hosting` (Surge references Core, so it
  can extend `Core.Hosting.NodeBackgroundService` and keep `IProcessor`).
- 2026-07-22 — `TokenCompletionSource` is **reimplemented in Core, not moved** — it is package code,
  not repo source. The replacement wraps `CancellationTokenSource` + `TaskCompletionSource<CancellationToken>`
  and is built with `RunContinuationsAsynchronously`, because `Complete()` is called from
  `NodeLifetimeService.Handle` — i.e. on the bus dispatch thread. Its contract was derived from
  `NodeLifetimeServiceTests`: the token handed to the waiter must be the *same* token a later
  `Cancel()` revokes, and `Cancel()` must also release a waiter that never got the signal so the
  dispose path cannot hang.
- 2026-07-22 — `SystemStartupManager` became `public` (was `internal`) for the same
  cross-assembly reason as the startup-task types.

## Open threads

- **`SystemBackgroundService` is dead code** (zero subclasses). It moved to Core with the rest rather
  than being deleted — deletion is still Sérgio's call.
- **`ISystemReadinessProbe` is declared but never implemented.** `SystemReadinessProbe` does not list
  it among its interfaces and nothing consumes it. Preserved verbatim; worth resolving.
- **The test runner cannot execute xUnit/NUnit assemblies at all.** It always passes the TUnit-only
  `--treenode-filter`, which those assemblies reject during argument parsing — they report
  "Zero tests ran" and contribute nothing to the report, silently. `KurrentDB.Connectors.Tests`
  (`IsKurrentXUnit=true`, 150 tests) is the significant casualty. Worth fixing in the runner: a green
  run currently proves nothing about any xUnit project.
- **`SystemReadinessProbe` subscribes in its constructor**, so a lazily-resolved singleton that is
  first constructed *after* the node has already published `BecomeLeader`/`BecomeFollower` would wait
  forever. Pre-existing in all three copies; not introduced here.
- **Connectors' leader lineage stayed put** — `LeaderNodeBackgroundService`, `LeaderNodeProcessorWorker`,
  `NodeLifetimeService` are single-copy Connectors-only code. Moving them would be relocation of
  shipping code, not deduplication. Open whether they follow the chassis into Core.
- **`KurrentDB.Connectors.Tests` never executes under the unit filter** — it is an xunit project and
  `--treenode-filter "/*/*/*/*[Category!=Integration]"` matches nothing there ("Zero tests ran").
  Connectors is therefore compile-verified only. Pre-existing gap, surfaced by this work.

## Verification

- `dotnet build KurrentDB.slnx -c Release` — 0 errors, 0 warnings.
- Unit category: SchemaRegistry 113 passed / 13 skipped, Kontext 400, DuckLance 459, Api.V2 366,
  Projections.V2 69, Core.TUnit 6. Integration category: 74/74.
- **Connectors 150/150**, run by invoking its MTP executable directly because the runner cannot reach
  xUnit assemblies. Includes the 27 tests in `KurrentDB.Connectors.Tests.System` that specify
  `NodeLifetimeService` and `LeaderNodeBackgroundService` — the direct gate on the reimplemented
  `TokenCompletionSource` and on the Surge-hosted leader base.
- `KurrentDB.Ammeter` fails 223 unit tests on macOS, all at 0ms, from a hardcoded Windows path in its
  config (`D:/Kurrent/cluster/certs/node3/node.key`). Pre-existing; its integration tests pass.

## Outcome

Six files now live in `src/KurrentDB.Core/Hosting/`: `NodeBackgroundService`, `SystemReadinessProbe`,
`NodeSystemInfo`, `NodeSystemInfoProvider`, `NodeSystemInfoWireUpExtensions`, `SystemStartupTasks`.
All three module-local copies are deleted and their consumers retarget to `KurrentDB.Core.Hosting`
via using-statement changes only — 17 changed lines across 14 files, plus two signatures flattened
from `NodeSystemInfo.NodeSystemInfo` to `NodeSystemInfo`.
