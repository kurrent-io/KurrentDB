---
name: sergio-csharp-style-law
description: "Sérgio's C# style rulings from the DuckLance cleanup (2026-07-18) — docs, guards, idioms"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 5efcc2a3-5207-42e4-8b2d-4438c5af3a19
  modified: 2026-08-15T15:31:27.711Z
---

Style rulings Sérgio issued while reformatting DuckLance; apply to all code I write in this repo.

- **Non-public XML docs are summary-only or absent.** Internal/private types and members get one
  concise `<summary>` (1–2 lines) or nothing. No `<param>`/`<returns>`/`<exception>`/`<remarks>`
  ceremony. Truly important knowledge moves INSIDE the member as `//` comments at the code it
  governs. Private fields use `//`, not `///`.
- **Comments are written for a junior developer**: plain language, one idea per block,
  cause-and-effect spelled out — "I know for you it's the same, but I am just a human."
  NEVER dense run-on comment paragraphs with lines cut mid-thought (2026-07-20): prefer a short
  lead sentence + bullets, one fact per bullet, blank comment lines between groups. Less text,
  same info.
- **Local functions whenever they fit** — and "fit" includes single-caller private helpers:
  a helper only one member calls belongs INSIDE that member as a local function, never as a
  sibling private method. VIOLATED 2026-08-13 (DuckDBDataSource.Mint: wrote MintCore +
  IsBenignAttachRace as private methods; Sérgio reshaped them into locals himself — "you keep
  forgetting we have local functions"). VIOLATED TWICE MORE 2026-08-15 (IsVectorIndex written
  class-level with one caller; then a full-file rewrite HOISTED IsBelowTrainingFloor back to
  class level after Sérgio had already localized it himself — "there will be consequences").
  TWO CHECKS, EVERY TIME: (1) before adding any private method, count callers — exactly one →
  it is a local function inside that caller; (2) during any rewrite/move, re-run the count and
  NEVER hoist an existing local function out — hoisting reintroduces what he removed.
- **Modern C# 14 / .NET 10 idioms always** — "we don't need to code like the year 2000":
  `ArgumentException.ThrowIfNullOrEmpty(x)` over manual `IsNullOrEmpty` + throw; raw string
  literals over concatenated SQL; expression bodies when a guard removal leaves one statement.
- **No null guards on NRT-non-nullable parameters** (`ThrowIfNull`, `Verify.NotNull`) — dead code;
  also delete tests that assert those throws (passing `null!` tests a dead path).
  VIOLATED TWICE 2026-08-07 by copying guards from sibling methods in the same file — Sérgio
  furious both times. Neighboring code carrying guards is a DEFECT to ignore, never a pattern to
  match; this rule outranks any file's existing shape. Check every new method signature against
  this list BEFORE writing the body.
- **Inside an internal class, members are `public` unless genuinely `private`.** The class already
  caps effective visibility; member-level `internal` is redundant noise.
- **Never duplicate a whole SQL statement when only one clause differs** (2026-07-20, "JUST CHANGE
  THE WHERE CLAUSE"): one shared `const` statement body; pick the differing clause via
  INTERPOLATION (`$"{sql}\nWHERE …"`), never `+` concatenation; compose the final `commandText`
  OUTSIDE the execute callback — the lambda only assigns it. Applies to WHERE variants, ASC/DESC,
  and any single-clause fork.

**Why:** he is hand-tuning this codebase to his taste before deciding whether it ships; churn
against these rulings costs review energy.

**How to apply:** when writing new code or sweeping old, check each: docs slim? guards modern and
NRT-trusting? idioms current? comments human-readable? Related: [[kontext-kurrentdb-integration-exploration]].
