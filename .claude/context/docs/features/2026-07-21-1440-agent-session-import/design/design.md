---
title: Agent Session Import
status: settling
authors: [sergio]
date: 2026-07-21
tags: [kontext, duckdb, agent-data, sessions]
---

# Design Space — Agent Session Import

> Working doc. Brainstorm, discussion, and decisions for this feature. Deliberately informal and
> **append-leaning** — you add to it, you mark decisions, you do not rewrite the history of the
> discussion. Kept for the life of the feature. Once it settles, distill the outcome into `prd/prd.md`
> and `spec/spec.md`, and slice releases into `plans/`. This doc is also the feature's decision record —
> keep the rejected options; the "why not" is the value.

## Problem / Trigger

Sérgio wants Claude Code agent sessions importable into Kontext, using the DuckDB
[`agent_data` community extension](https://duckdb.org/community_extensions/extensions/agent_data)
([source](https://github.com/axsaucedo/agent_data_duckdb)) — SQL table functions over `~/.claude`
session data. The suggested shape: a background service that runs the import on a cadence.

## Exploration

### The extension (validated 2026-07-21, macOS arm64)

- Rust-based community extension; `INSTALL agent_data FROM community; LOAD agent_data;`.
- Table functions: `read_conversations()`, `read_plans()`, `read_todos()`, `read_history()`,
  `read_stats()`. All default to `~/.claude`; explicit form is
  `read_conversations(path := '...', source := 'claude')` — **both named args required** on the
  explicit overload (single positional arg is a binder error).
- `read_conversations` emits one row per JSONL line, 27 columns: identity (`uuid`, `parent_uuid`,
  `session_id`, `file_name`, `line_number`), content (`message_role`, `message_content`, `model`,
  `tool_name`, `tool_input`), usage (`input/output/cache_*_tokens`), context (`git_branch`, `cwd`,
  `slug`, `repository`, `version`, `stop_reason`). Sub-agent transcripts under
  `projects/<project>/<session>/subagents/agent-*.jsonl` are included (`is_agent`).
- `timestamp` is VARCHAR (ISO-8601 Z) — cast to TIMESTAMPTZ on import.
- Unknown JSONL line types surface as `message_type = '_parse_error'` rows, not failures. On
  Sérgio's live data (~326k rows, 2751 sessions since April) ~28% are parse errors — ALL benign
  metadata line types the extension doesn't model yet (`attachment`, `last-prompt`,
  `permission-mode`, `ai-title`, `mode`, `worktree-state`, `file-history-delta`, `agent-name`).
  Conversation messages parse fine; the importer filters `_parse_error` out.
- **In-process load works through DuckDB.NET.Data.Full 1.5.3** (the version Kontext pins) — the
  community repo builds per engine version, and 1.5.3 has a build (`design/spike-inprocess-load.cs`).
  Caveat: the extension build you get is tied to the bundled engine version; the 1.5.3 build is
  older than the 1.5.4 one (row counts differ slightly — fewer known line types).
- No predicate pushdown: every query re-scans all JSONL files. Full scan of ~600 MB of history
  costs ~5–12 s — fine on a background cadence, wrong on a request path.
- `INSTALL ... FROM community` needs network the first time (cached under `~/.duckdb` after);
  an offline tick must degrade to a logged failure, not a crash.

### Import SQL (validated end-to-end, `design/spike-import-tick.sql`)

One idempotent command batch per tick: `INSTALL`/`LOAD` (no-ops when present), `CREATE SCHEMA IF
NOT EXISTS sessions`, explicit `CREATE TABLE IF NOT EXISTS sessions.messages` (27 extension columns
+ `imported_at`), then `INSERT ... BY NAME SELECT ... ANTI JOIN sessions.messages ON uuid`.

- Incremental key is `uuid` (message identity). Forked/resumed sessions copy prefix messages into
  new files, so the incoming scan itself is deduped too
  (`QUALIFY row_number() OVER (PARTITION BY uuid ...) = 1`).
- Live-proven: first run imported 190,149 messages; 19 rows appended to `~/.claude` between runs
  (the working session itself); second run imported exactly the new tail. `rows == distinct uuids`
  after both runs.
- Explicit DDL over `CREATE TABLE AS ... LIMIT 0` on purpose: an extension upgrade that adds
  columns must not silently reshape our table; unselected new columns are simply ignored.
- Anti-join over a PK + `INSERT OR IGNORE`: same result, no constraint maintenance cost on a
  table this hot, and the pattern reads as what it is — "insert the missing tail".

### Where it lands

Raw relational tables in the Kontext engine catalog (`sessions.messages`), NOT the `ldb` Lance
namespace — Lance is the vector side; plain session history is plain DuckDB SQL (the
engine-per-operation rule). Memory distillation from sessions is explicitly out of scope here —
a later feature can feed on these tables.

### Component shape

Mirror the settled V2 data-layer split (`KontextSchema` / `KontextMaintenanceScheduler`):

- `AgentSessionImporter` — owns ALL SQL: the one idempotent per-tick command batch, plus the
  count/state probes tests need. Runs through `KontextConnectionPool.ExecuteAsync`. The pool's
  rented surface is documented as the READ surface because Lance writers need commit-conflict
  retry and prepared-statement reuse — neither applies to a plain-table INSERT, and the importer
  is serialized by its scheduler gate, so renting is safe and keeps the importer stateless.
- `AgentSessionImportScheduler` — owns the clock: `TimeProvider` timer, non-overlap tick gate,
  interlocked disposal, never-throws tick body, `TickNowAsync` for deterministic tests. Same
  skeleton as `KontextMaintenanceScheduler`.
- `AgentSessionImportOptions` — mutable settings class (config binding does not cope with
  records): `SourcePath` (default `<user profile>/.claude`, resolved in .NET — never rely on
  duckdb tilde expansion; the server process HOME is not the agent's), `TickInterval`
  (default 5 min), `LoggerFactory`.

Host DI wiring is deferred on purpose: the whole V2 data layer (`KontextSchema`,
`KontextMaintenanceScheduler`, `KontextDataStoreV2`) is currently test-driven and not yet
registered in `KontextServiceCollectionExtensions` (still on the V1 `VectorStore` store). The
importer joins at the same maturity and gets wired when the V2 registration lands.

## Decisions

- 2026-07-21 — Import target is **raw session tables** over distill-into-memories: the extension's
  rows copied incrementally; memory distillation stays a separate later decision. (Sérgio, via
  scoping question.)
- 2026-07-21 — Placement is a **Kontext background service** modeled on
  `KontextMaintenanceScheduler`, over a standalone script/tool. (Sérgio, via scoping question.)
- 2026-07-21 — `agent_data` is loaded **inside the importer's command batch**, not in
  `KontextConnectionPool.Initialize`: core memory ops must not depend on a community extension
  whose install needs network.
- 2026-07-21 — Scope is `read_conversations` only. `read_todos`/`read_history`/`read_plans` are
  the same pattern and can be added when something needs them.
- 2026-07-21 — Claude Code only (`source := 'claude'`). The extension supports Copilot/Codex/
  Gemini; nothing asked for them.
- 2026-07-21 — Implementation landed (Opus subagent, additive-only): `AgentSessionImporter`,
  `AgentSessionImportScheduler`/`AgentSessionImportOptions` in `Kurrent.Kontext/Data/`, plus 7
  integration tests — all green (run `e3b1b3b4253144979936fe023036bec4`). One documented deviation:
  `CountAsync` probes `duckdb_tables()` in a separate command because `SELECT count(*)` binds the
  table name at parse time and would throw before the first import creates it.

- 2026-07-21 — Parse-error rows are **kept, not dropped** (Sérgio): a second table
  `sessions.parse_errors` (source, session_id, project_path, project_dir, file_name, line_number,
  error, scanned_at) with **snapshot semantics** — `CREATE OR REPLACE` per tick, so the table
  always means "lines the current extension build cannot parse"; when a newer build learns a line
  type, those lines graduate into `sessions.messages` (their raw uuids become parseable) and drop
  out of the snapshot automatically. Linkage is a **logical join on `session_id`** (plus
  `file_name`/`line_number` to pin the source line), not an enforced FOREIGN KEY:
  `messages.session_id` is not unique so a real FK would require inventing a sessions dimension
  table, and 4 observed sessions consist ONLY of unparseable lines — an enforced FK would reject
  exactly the rows we want to keep. Both tables now split ONE materialized scan per tick
  (`CREATE OR REPLACE TEMP TABLE`) since the extension re-reads every file per call — halves the
  scan cost. Validated end-to-end (`design/spike-import-tick.sql`, updated in place).

- 2026-07-21 — Polish pass (Sérgio): tables renamed to **`transcripts`** + **`transcript_parse_errors`**
  and moved to the **engine catalog's `main` schema** — no schema creation at all. Rejected the
  literal "same schema as memories" (`ldb.main`): tables in the Lance namespace ARE Lance datasets,
  which would drag every import tick into Lance's optimistic-commit write path for a plain
  bulk-append workload. **DDL moved out of the tick** into a one-time `CreateAsync` bootstrap
  (mirroring `KontextSchema.CreateAsync` + quiet-skip ticks before bootstrap, exactly like the
  maintenance scheduler); the parse-errors snapshot refresh becomes `DELETE FROM` + `INSERT`
  (stable table identity, tick is DML-only). No indexes on purpose: zone maps cover these scans;
  nothing here benefits from ART. Validated: `spike-bootstrap.sql` (idempotent) +
  `spike-import-tick.sql` (updated in place).

## Open Questions

- Reader technology for the next phase: see [[2026-07-21-2015-fad-dotnet-aot-wrapper]] — the
  probe-gated evaluation of wrapping franken_agent_detection for .NET AOT (vs a pure C# kcap-style
  reader). The extension-based reader this feature shipped with is superseded in spirit either way.

- When the V2 registration point lands in `KontextServiceCollectionExtensions`, wire the importer
  (and decide whether it's on by default or opt-in config).
- Extension-version drift: the 1.5.3 community build lags the 1.5.4 one. Revisit when DuckDB.NET
  bumps — parse-error coverage improves with newer builds.
- Sub-agent transcripts (`is_agent = true`) are imported. Keep, or filter to primary sessions
  only? Kept for now — cheap, and reflection over delegation patterns may want them.
