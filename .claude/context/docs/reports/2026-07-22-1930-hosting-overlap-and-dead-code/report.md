---
title: Core.Hosting — behavioural overlap, dead code, and latent defects
type: audit
date: 2026-07-22
author: sergio
tags: [core, hosting, readiness, dead-code, shutdown]
scope: src/KurrentDB.Core/Hosting, src/KurrentDB.Surge/Hosting, and every readiness/lifecycle consumer in the repo
related: [2026-07-22-1315-system-readiness-unification]
---

## Summary

Four parallel read-only investigations audited the newly consolidated node-hosting components for
behavioural overlap and unused code. Headline results:

- **Six symbols are provably dead**, each verified as a single repo-wide hit at its own declaration.
- **The readiness message ordering is configuration-dependent**, and nothing documents or enforces it.
  This corrects an earlier claim made during the consolidation.
- **Five latent defects** were found that are not dead code, two of which affect running behaviour today
  and one of which will break the Kontext projector the moment it is registered.
- **`NodeBackgroundService` is ~90% a verbatim copy of `Microsoft.Extensions.Hosting.BackgroundService`**,
  and because it is a bare `IHostedService` rather than a `BackgroundService`, post-startup crashes in
  any node background service are invisible to the host.

## Findings

### 1. Correction: the readiness ordering is inverted from what was previously stated

During the consolidation it was claimed that the probe's `BecomeLeader|BecomeFollower|BecomeReadOnlyReplica`
gate fires *later* than `SystemMessage.SystemReady`. **That is backwards in the default configuration.**

Verified chain:

- `src/KurrentDB.Core/Authentication/InternalAuthentication/InternalAuthenticationProvider.cs:136` —
  `public override Task Initialize() => _tcs.Task;`
- `src/KurrentDB.Core/Authentication/InternalAuthentication/UserManagementService.cs:226,230,233` —
  that same `_tcs` is completed **only** from `Handle(BecomeLeader)`, `Handle(BecomeFollower)`,
  `Handle(BecomeReadOnlyReplica)`.
- `src/KurrentDB.Core/Services/VNode/ClusterVNodeController.cs:622` — `AuthenticationProviderInitialized`
  → `_startSubsystems()` → `SystemCoreReady`; line 634 → `SystemReady`.

So `SystemReady` is transitively gated *behind* the role transition. Ordering by configuration:

| Configuration | Order |
|---|---|
| Default / secure (`InternalAuthenticationProvider`) | `Become*` **before** `SystemReady` |
| `--insecure` (`PassthroughAuthenticationProvider`, `ClusterVNode.cs:965`) | inverts — `Initialize()` returns `Task.CompletedTask` |
| External auth plugin (OAuth/LDAPS) | inverts, unless projections are enabled (which re-imposes a role gate via `ProjectionsSubsystem.cs:277-303`) |

Neither signal is unconditionally stronger. The invariant "`SystemReady` implies the node has a role" is
load-bearing for at least four components and is enforced by nothing.

### 2. Confirmed dead code

Each verified as exactly one repo-wide hit — the declaration itself.

| Symbol | Location | Notes |
|---|---|---|
| `SystemBackgroundService` (whole file) | `Core/Hosting/SystemBackgroundService.cs` | Zero subclasses, zero DI registrations. See §4 for why it should not simply be adopted instead. |
| `ISystemReadinessProbe` | `Core/Hosting/SystemReadinessProbe.cs:13-15` | `SystemReadinessProbe` does **not** list it in its base clause. Orphan interface. |
| `LogNodeStateChangeIgnored` | `Core/Hosting/NodeLifetimeService.cs:93-94` | Generated log method, never called. `internal`, so zero API risk. |
| `AddSystemStartupTask(string, Func<…>)` + `SystemStartupTaskProxy` | `Core/Hosting/SystemStartupTasks.cs:57-68, 84-87` | Only the zero-arg generic overload has callers. Class is `[PublicAPI]` — the delegate form is the most plugin-friendly, so weigh intent before deleting. |
| `NodeBackgroundService.ExecuteTask` | `Core/Hosting/NodeBackgroundService.cs:31` | Never read. May be deliberate parity with `BackgroundService`. |
| `GetNodeLifetimeService` delegate | `Core/Hosting/NodeLifetimeService.cs:18` + `ControlPlaneWireUp.cs:31` | Registered in DI, never resolved. A factory nobody calls. |
| `SystemSensor<T>` | `Connectors/Infrastructure/System/SystemSensor.cs` | Orphaned leftover of the abandoned `MessageModule` design. |
| `MessageModule`, `IMessageBus`, `SubscriberExtensions.On<T>` | `Connectors/Infrastructure/System/Messaging.cs:28, 11, 61-72` | Orphaned. The rest of the file is live — see §5. |

`NodeSystemInfo` deserves separate mention: of its seven public members, **only `InstanceId` is read outside
the file**. `Timestamp` is assigned and never read; `IsNotLeader` never read; `IsLeader` read only to compute
`IsNotLeader`; both endpoint properties construct a `DnsEndPoint` per instance for nobody. Caveat: it is a
`record struct`, so compiler-generated equality reads every field and
`Connectors.Tests/.../LeaderNodeBackgroundServiceTests.cs:50,87` compares by equivalence — trimming members
changes value semantics.

### 3. Latent defects (not dead code)

Ranked by blast radius.

**3.1 `ServiceName` collision will break the Kontext projector on registration.**
`SchemaRegistry/.../DuckDBProjectorService.cs:16` and `Kontext/.../Memory/DuckDBProjectorService.cs:24` both
pass the literal `"DuckDBProjector"`. `ShutdownService.cs:53-54` throws
`InvalidOperationException($"Component {name} already registered")` on a duplicate. Today the Kontext copy is
untracked and unregistered, so this is latent — but it fires the moment both are hosted in one node.

**3.2 Post-startup crashes are invisible to the host.**
`NodeBackgroundService` is a bare `IHostedService`, not a `BackgroundService`. .NET's host applies
`BackgroundServiceExceptionBehavior.StopHost` only to `BackgroundService` instances, so when
`ExecuteAsync` faults after startup the exception is logged at `NodeBackgroundService.cs:61`,
`ComponentTerminated` is published (silently deregistering the component from `ShutdownService`), and the
node keeps running without it. Affects `ConnectorsControlService` and the SchemaRegistry projector today.

**3.3 An unmatched `ComponentTerminated` throws.**
`NodeBackgroundService.cs:65` publishes `ComponentTerminated` unconditionally in `finally`, but
`ShutdownService.cs:47-51` *silently drops* a registration that arrives while already shutting down. The
later `ComponentTerminated` then hits `ShutdownService.cs:92-93` → `throw new InvalidOperationException`.
Any component started during shutdown hits this. Cheapest fix: log-and-return instead of throw.

**3.4 `NodeLifetimeService` is never deterministically disposed in production.**
`LeaderNodeBackgroundService` holds it as `INodeLifetimeService`, which does not extend `IDisposable`, and
nothing disposes it. Only the finalizer (`NodeLifetimeService.cs:86`) cleans up; the bus subscription taken
at line 34 is never released. Tests dispose it; production does not.

**3.5 `SystemReadinessProbe.WaitUntilReady` does uncancellable I/O.**
Line 43 calls `GetNodeSystemInfo()` without forwarding the `CancellationToken`, and
`NodeSystemInfoProvider.cs:24-27` does a gossip read with a `lastEvent!.Value` non-null assertion and a
`.Single(...)` that throws if the node is not yet in its own gossip. Unbounded and uncancellable on the
shutdown path. It also leaks its subscription on the cancellation path (unsubscribe happens only on success).

### 4. The one place the investigations disagreed

One agent recommended **deleting** `SystemBackgroundService` (dead); another recommended
`WorkspaceProjection` and the SecondaryIndexing services **adopt** it. Both are partly right, and the
resolution is neither: the *capability* (a readiness-gated hosted service) is genuinely wanted by at least
three consumers, but the class as written extends Microsoft's `BackgroundService` and therefore **opts out of
graceful termination** — adopting it as-is would spread defect 3.2 rather than fix it.

Correct move: fold the readiness gate into `NodeBackgroundService` as an option, delete
`SystemBackgroundService`, and let the would-be adopters take the option.

### 5. Duplication still outstanding

| Item | Verdict |
|---|---|
| `DuckDBProjectorBackgroundService` (`SchemaRegistry/.../DuckDBProjectorService.cs:39`) | The last committed duplicate of the readiness idea; its own `TODO` still asks for this fix. Blocked on a "wait for ready, don't tell me who I am" probe overload — it would otherwise inherit a gossip read it discards. |
| `Messaging.cs` declares `namespace KurrentDB.Core.Bus` **from inside the Connectors assembly** | Its live third (`MessageHandler<T>` + two delegates) is consumed by both `Connectors.Tests/MessageBus.cs` and `SchemaRegistry.Tests/MessageBus.cs`, so SchemaRegistry's tests depend transitively on a Connectors production type squatting in a Core namespace. Exactly the coupling the consolidation set out to remove. |
| `SchemaRegistryStartupTask.cs` and `SchemaMessageRegistrationStartupTask.cs` | Line-for-line copies of each other, including the same commented-out log line. Same copy-paste debt, different axis (schema registration, not node readiness). |
| AutoScavenge (`GossipAwareBase.cs`, `NodeState.cs`, `AutoScavengeProcessManager.cs:154-177`) | Strongest duplicate in the repo — independently reimplements gossip reading, `IsLeader`, and leadership gain/loss transitions, with `State` stringly-typed and compared against `"Leader"`. Different transport (`POC.IO.Core.IClient`), so not a drop-in. |
| Three private copies of the `$GossipUpdated` payload DTO | `NodeSystemInfoProvider.cs:35`, AutoScavenge `GossipMessage.cs:6`, `GossipMonitor.cs:189-192`. One schema change from breakage. |
| `Kontext/WorkspaceProjection.cs:28-50`, `KontextInitializer.cs:62-79` | Hand-rolled readiness by `Task.Delay(5s)` and a `ServerNotReady` retry poll. Should adopt a real gate. |
| `SecondaryIndexing` `DefaultIndexBuilder.cs:18`, `UserIndexEngine.cs:26` | `IHandle<SystemReady>` + fire-and-forget `Task.Run`; exceptions unobserved, `StopAsync` cannot cancel, no graceful-termination registration. |

Checked and found genuinely different — leave alone: `ProjectionsSubsystem` (carries a multi-component state
machine the abstraction has no notion of), `GossipMonitor`'s core loop (needs a live view, not a one-shot
snapshot), `KontextReadySignal` (means "Kontext is wired", not "node is up"), `Api.V2` and `Projections.V2`
(no readiness coupling at all).

### 6. Is `NodeSystemInfo` a duplicate of an existing Core type?

No. It wraps `ClientClusterInfo.ClientMemberInfo` and adds self-identification plus assembled `DnsEndPoint`s.
The closest existing type is `VNodeInfo` (`Core/Data/VNodeInfo.cs:10`), which is **static configuration** —
addresses only, no `VNodeState`, no `IsAlive`, no timestamp. `NodeStatusTracker` holds current state but is
write-only (`OnStateChange`, no getter). So Core genuinely lacks a live "who am I in the cluster right now"
type; `NodeSystemInfo` fills a real gap, thinly.

The more useful observation: **`IsLeader`'s definition is reimplemented in three places** —
`NodeSystemInfo.cs:20`, AutoScavenge `GossipAwareBase.cs:16`, `GossipMonitor.cs:58,150`. Hoisting that
predicate onto `ClientMemberInfo` would let all three converge without moving anyone's plumbing.

## Recommendations

Ordered by value-to-risk.

1. Delete the six confirmed-dead symbols in §2 (pending a call on `[PublicAPI]` surface).
2. Fix 3.1 before wiring the Kontext projector — give the two projectors distinct `ServiceName`s.
3. Fix 3.3 (`ComponentTerminated` log-and-return) — one line, removes a DEBUG-fatal path.
4. Decide on 3.2: either derive `NodeBackgroundService` from `BackgroundService` (gaining host fault
   monitoring) or accept that node background services fail silently.
5. Fold the readiness gate into `NodeBackgroundService` as an option; delete `SystemBackgroundService`.
6. Add a `SystemReady`-vs-`Become*` ordering test, or at minimum a comment at
   `InternalAuthenticationProvider.cs:136`, pinning the invariant four components already rely on.

## Method

Four parallel read-only agents: usage census (34 tool calls), readiness-signal overlap (65), lifecycle chassis
duplication (49), leftovers and undiscovered copies (54). Structural search via `ast-grep` plus ripgrep sweeps;
the chassis audit diffed against the current `dotnet/runtime` `BackgroundService` source.

Cross-checked by hand before publication: the `SystemReady` ordering chain, the `ShutdownService` throw, and
the `"DuckDBProjector"` name collision. Findings asserted by two or more independent agents are marked
confirmed; single-source structural claims are marked as such.

**Not covered:** no build or test run was performed (read-only). Whether `SystemBackgroundService` or
`ISystemReadinessProbe` are consumed by out-of-tree plugins is unverifiable from this repo. Whether
`ISystemClient` writes from a startup task succeed on a Follower/ReadOnlyReplica — i.e. whether the probe's
role-blindness causes silent failures on non-leader nodes — was not traced and is the most valuable open
question. The untracked Kontext `Modules/Memory/` tree was treated as work in progress and not audited.
