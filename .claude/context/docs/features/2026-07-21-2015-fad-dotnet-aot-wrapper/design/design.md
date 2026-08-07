---
title: franken_agent_detection .NET AOT Wrapper
status: exploring
authors: [sergio]
date: 2026-07-21
tags: [kontext, sessions, ffi, rust, aot, agent-detection]
---

# Design Space — franken_agent_detection .NET AOT Wrapper

> Working doc. Brainstorm, discussion, and decisions for this feature. Deliberately informal and
> **append-leaning**. Related feature: [[2026-07-21-1440-agent-session-import]] — this wrapper is a
> candidate reader technology for that feature's next phase (and for multi-agent breadth beyond it).

## Problem / Trigger

The agent-session-import investigation (2026-07-21) surveyed everything that "knows how to read
agent sessions": the `agent_data` DuckDB extension (rejected: full rescan per query, full in-memory
materialization, stale line-type enum), kcap-server (production event pipeline, 9 vendors,
canonical `kurrent.agent.v2` protobuf — ours), and
[franken_agent_detection](https://github.com/Dicklesworthstone/franken_agent_detection) (Rust, MIT,
~30 per-agent connectors, mtime-incremental scans, streaming, conformance tests — the broadest
open per-agent reader library). Clones for reference: `~/dev/contrib/franken_agent_detection`
(at cass's pinned rev `6d24c532`, which IS current main), `~/dev/contrib/coding_agent_session_search`,
`~/dev/kurrent/kcap-server`.

Question on the table: how hard is a .NET 10/11 **Native AOT-compatible** wrapper exposing the
franken operations Kontext needs — and what must be proven before committing?

## Exploration

### Verified facts (2026-07-21, from source)

- **Pure Rust rlib — no C ABI anywhere** (no `cdylib`/`staticlib` crate-type, no `extern "C"`,
  no `no_mangle`). A shim crate is mandatory; there is nothing to P/Invoke today.
- **Every canonical type derives serde** (`NormalizedConversation`, `NormalizedMessage`,
  `NormalizedInvocation`, `NormalizedSnippet` — `src/types.rs`). A JSON boundary costs one
  `serde_json::to_string` in the shim and zero hand-marshaling.
- **The API surface Kontext would want is small**: `detect_installed_agents()`,
  `Connector::scan(ctx)` (honors `since_ts` mtime gating — `claude_code.rs:287,449`),
  `Connector::discover_source_files(ctx)` (pre-parse file discovery, "mirror precious source
  artifacts before parser failures can make a session invisible"), and per-vendor constructors.
- **`scan_with_callback` streaming exists** but crosses FFI as a function pointer — deferred; v1
  uses batch `scan()` + `since_ts`, optionally bounded per `ScanRoot` to cap memory.
- **Toolchain**: `rust-toolchain.toml` pins **nightly** (components rustfmt/clippy — possibly
  convenience, not necessity; edition 2024 is stable since Rust 1.85). Local machine currently has
  NO Rust toolchain — probes need `rustup` first.
- **Features**: `default = []`; `connectors` is opt-in, plus per-vendor feature flags (cass enables
  `connectors, cursor, chatgpt, opencode, crush, hermes` — Claude/Codex ride in base `connectors`).
- **License MIT**; version 0.1.10; fast-moving (cass pins by git rev — we would too).

### Proposed architecture (to be validated by the probes)

```
franken-agent-detection (pinned rev)
        │  rlib
   fad-ffi shim crate (~150 lines Rust)          ← we own this
        │  cdylib: extern "C" { detect, scan, discover_files, free_string }
        │  JSON strings across the boundary
   Kurrent.AgentDetection.Native (.NET)          ← we own this
        │  [LibraryImport] bindings (source-gen, AOT-safe)
        │  STJ source-gen models mirroring Normalized*
   consumers (Kontext importer, anything else)
```

Packaging: per-RID native assets in NuGet `runtimes/` — the DuckDB.NET pattern; Kontext already
ships DuckDB, Lance, and ONNX natives, so the motion is known. The RID matrix (osx-arm64/x64,
linux-x64/arm64, win-x64) is the bulk of the real cost, in CI not code.

### Fit with agent-session-import — name the tension

`scan()` yields **normalized user/assistant messages only** (`claude_code.rs:516` filters to
`user | assistant`; no `attachment`, `ai-title`, metadata types). That is NARROWER than the
raw-first `transcripts` decision. So the wrapper does not replace the raw reader — it complements
it: `discover_source_files()` serves raw-first discovery across ~30 agents; `scan()` serves
normalized multi-agent message views when/if breadth matters. If Kontext stays Claude-only,
a pure C# reader (kcap-style discovery + watermark + validated projection) is LESS total work
than this wrapper. The wrapper's value = ~29 parsers we never write, conformance-tested upstream.

## Probes — execute before any commitment

Ordered kill-risk-first. Each: the claim it tests → method → success gate. A failed early probe
stops the line cheaply.

- **P0 — Toolchain baseline** (30 min). *Claim: the crate builds without the nightly pin.*
  `rustup` install; `cargo +stable build --features connectors` at the pinned rev.
  Success: stable ≥1.85 builds clean. Failure → nightly-in-CI risk goes on the decision scales.
- **P1 — Shim exports** (½ day). *Claim: a cdylib shim over the crate links and exports.*
  Write `fad-ffi` (scan/detect/discover/free_string, JSON out); build osx-arm64;
  `nm -gU` shows the four symbols; a C smoke call returns JSON.
- **P2 — .NET round-trip on fixture** (½ day). *Claim: LibraryImport + STJ source-gen consume the
  boundary losslessly.* File-based C# app; scan the validated spike fixture
  (`agent-session-import/design/spike-fixture-session.jsonl` in a temp `projects/` layout);
  assert 3 messages, roles, and the Bash tool invocation (name + call_id + arguments) survive.
- **P3 — Real-corpus scale & memory** (1 h). *Claim: batch scan of ~340k lines is tolerable.*
  Scan the real `~/.claude` via the shim; `/usr/bin/time -l` wall + peak RSS; compare against the
  native `read_ndjson` baseline (1.3 s). Gates: completes; peak RSS documented and acceptable;
  repeat with per-ScanRoot batching to show memory is boundable.
- **P4 — Incremental gate through FFI** (15 min). *Claim: `since_ts` mtime pruning survives the
  boundary.* Scan with `since_ts = now`: near-empty result, sub-second wall time.
- **P5 — NativeAOT publish** (½ day). *Claim: the whole .NET side is AOT-clean.*
  `PublishAot=true` on the P2 consumer; zero IL2026/IL3050 from our code (aot-report discipline);
  published binary runs P2 successfully.
- **P6 — Ownership soak** (30 min). *Claim: string ownership across the boundary doesn't leak.*
  1,000 repeated fixture scans in-process; RSS flat; `free_string` invoked per call.
- **P7 — Linux cross-build** (½–1 day, deferrable to CI phase). *Claim: the RID matrix is
  buildable.* linux-x64 cdylib in a container; loaded by a .NET container run of P2. Go/no-go for
  packaging; not required for the local go/no-go.
- **P8 — Rev-bump conformance golden** (1 h, test strategy). *Claim: upstream drift is catchable.*
  Golden JSON of the fixture scan; a rev bump that changes output fails the golden loudly.

**Go/no-go**: P0–P5 green ⇒ the 1–2-week production estimate stands and the wrapper is viable.
P3 failing on memory ⇒ streaming-callback FFI becomes mandatory — re-estimate before proceeding.

## Decisions

<!-- Append decisions as you make them. Date each. Keep rejected options and the reason they lost. -->

- 2026-07-21 — Put the plan on paper with an explicit probe list before any implementation
  (Sérgio). No code until the probes say go.
- 2026-07-21 — Named and scaffolded: **mikoshi** at `~/dev/priv/mikoshi` (Cargo workspace +
  .NET slnx, probe-shaped skeleton, CLAUDE.md carries the probe table). .NET side builds clean;
  probe tests present and skipped.
- 2026-07-21 — **P0 PASSED**: franken-agent-detection 0.1.10 at the pinned rev compiles on
  **stable Rust 1.97.1** (24 s cold) — the upstream nightly pin is convenience, not necessity;
  mikoshi pins stable. Toolchain installed via `brew install rustup`; note the brew shims don't
  put cargo/rustc on PATH — use `~/.rustup/toolchains/stable-aarch64-apple-darwin/bin`.
- 2026-07-21 — **P1 PASSED**: the stub shim builds as a cdylib and `nm -gU` shows all four
  exports (`mikoshi_detect_agents`, `mikoshi_scan`, `mikoshi_discover_files`,
  `mikoshi_free_string`).
- 2026-07-21 — **GO: P0–P5 all green** (Opus subagent implemented the shim + wiring + probe
  tests; independently re-verified — 4/4 tests pass in Release). Evidence: P2 fixture round-trip
  lossless incl. Bash invocation w/ call_id + arguments; P4 `since=now+1h` → 0 conversations,
  sub-second; P6 1,000 scans, managed Δ<16 MB, RSS Δ<128 MB; P8 golden-JSON conformance in place;
  P5 NativeAOT publish → 2.6 MB native binary, 9 MB working set, ZERO IL2026/IL3050 from our
  code. **P3 pass-with-caveat**: full `~/.claude` (1.1 GB, 3,675 files) → 3,664 conversations /
  108,731 messages / 51,599 invocations in 10.9 s, but **peak RSS 3.13 GB (~2.8× corpus)** — the
  crate always serializes each message's `extra` (the entire raw JSONL record) + conversation
  `metadata` across the boundary. Tolerable today; the mitigation lever is the crate's
  `scan_with_callback` streaming path behind a callback FFI if corpora grow. Mikoshi's CLAUDE.md
  probe table carries the full status.
- 2026-07-21 — Upstream API surprises, recorded so nobody re-learns them: `since_ts` is
  **milliseconds** (shim exposes seconds and scales ×1000); detection entries serialize `slug`,
  NOT `agent_slug` (conversations DO use `agent_slug`); `DiscoveredSourceFile` doesn't derive
  Serialize upstream (shim hand-maps `{path, role}`); STJ source-gen ignores `= []` initializers
  on deserialize (absent collections → null; models coalesce with `get => field ?? []`); the
  .NET-facing slug is `"claude_code"` (crate factory key is `"claude"`; output stamps
  `agent_slug = "claude_code"`).

- 2026-07-21 — **P3's caveat ruled UNACCEPTABLE (Sérgio): the mandatory-streaming clause is
  invoked.** Batch materialization of the corpus is not shippable as the default path. New
  performance bar: peak memory O(largest single conversation), never O(corpus); zero-copy
  boundary; no bytes transferred that the consumer drops. Two new gated probes:
  - **P9 — streaming FFI**: `mikoshi_scan_stream(slug, roots_json, since_ts, callback, state)`
    over the crate's `scan_with_callback`; per-conversation UTF-8 payload as (ptr, len);
    shim STRIPS `extra`/`metadata` before serializing (we pay nothing for bytes we drop);
    .NET side `[UnmanagedCallersOnly]` function pointer, deserializing each conversation with
    `Utf8JsonReader`/span APIs directly off native memory — no intermediate UTF-16 string, no
    whole-corpus blob. GATE: full `~/.claude` scan peak RSS **< 500 MB** (target ≈ 250 MB),
    wall time ≤ the 10.9 s batch baseline, results identical to batch (same conversation/message
    counts).
  - **P10 — allocation discipline**: per-conversation managed allocation budget measured
    (`GC.GetTotalAllocatedBytes`), no LOH churn from boundary strings; batch API remains for
    small scopes/tests but streaming is the documented default for backfill-scale scans.

- 2026-07-21 — **P9 + P10 PASSED, gates crushed** (independently re-verified: 7/7 tests green;
  my own probe run showed peak footprint **99.5 MB**). Streaming scan of the full `~/.claude`:
  **peak RSS ~113 MB (28× below batch's 3.13 GB), wall 5.7 s (FASTER than batch's 10.8 s —
  no giant blob to build/marshal/parse)**, identical counts, ~113 KB managed alloc per
  conversation (the returned object graph — unavoidable while handing back objects).
  `[UnmanagedCallersOnly]` + `delegate* unmanaged` path is NativeAOT-clean (zero IL warnings).
  Batch `extra`-stripping alone measured for the record: 3.13 GB → 2.39 GB — the crate builds the
  full Vec before the shim can strip, confirming streaming (not stripping) is the real fix.
- 2026-07-21 — Upstream streaming surprise worth remembering: `scan_with_callback`'s DEFAULT
  trait impl is NOT streaming — it materializes via `scan()` and replays. Only connectors
  overriding it (`supports_streaming_scan() == true`; Claude Code does) genuinely stream.
  The memory win is **per-connector** — verify before assuming for other agents.

- 2026-07-22 — **P7 PASSED (local legs) — the probe table is closed.** linux-x64 `.so` built in
  a `rust:1-slim` container (37 s, amd64-under-emulation, no linker/glibc friction) and the FULL
  test suite ran **7/7 green inside `dotnet/sdk:10.0` x86_64** — cross-platform load + function
  proven without GitHub. P8's golden matched byte-identically on linux (JSON output is
  platform-stable). `scripts/pack.sh` implemented (DuckDB.NET `runtimes/<rid>/native/` layout,
  loud missing-RID warnings); nupkg layout verified. Real 5-RID workflow in
  `.github/workflows/native.yml` (dispatch + `v*` tags). Commits: `1e4e0d0` (wrapper),
  `2f20a58` (P7). **Residual risk, explicit:** the GitHub-runner legs (osx-x64, linux-arm64,
  win-x64 builds; artifact fan-in; runner labels) are correct-by-construction but unexecuted
  until first push; and NuGet's runtime-asset RESOLUTION from a consumed nupkg is unexercised —
  the layout is asserted, but no fresh consumer project has restored the package yet. Bonus
  de-risk: the .NET 10 GA SDK compiled `LangVersion=preview` + `field` keyword in-container.

## Open Questions

- Scope: is multi-agent breadth actually wanted for Kontext, or is Claude-first the real
  requirement? (Decides wrapper-vs-pure-C#-reader; see the tension section.)
- Where does the wrapper live — inside kurrentdb, or its own repo/package
  (`Kurrent.AgentDetection.Native`)?
- Nightly pin: if P0 fails on stable, do we accept nightly in CI or vendor a stable-compatible
  fork?
- Who owns the RID build matrix long-term (CI cost is the bulk of the estimate)?
